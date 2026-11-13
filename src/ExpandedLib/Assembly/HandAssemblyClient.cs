using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The client's side of hand assembly: takes the right click while a part and its tool are held, shows the ghost of the shown placement, and times the hold that sends a fix or a take-off.</summary>
internal sealed class HandAssemblyClient : IRenderer {
  private const int RecomputeMs = 100;
  private const string TakeOffKey = "takeoff";

  private const float BaseAlpha = 0.55f;
  private static readonly (float R, float G, float B) GhostTint = (
    0.8f,
    1f,
    0.8f
  );
  private static readonly (float R, float G, float B) ShortTint = (
    1f,
    0.35f,
    0.35f
  );
  private static readonly int JoinedColor = ColorUtil.ColorFromRgba(
    94,
    215,
    150,
    0x60
  );
  private static readonly int OpenColor = ColorUtil.ColorFromRgba(
    240,
    180,
    60,
    0x60
  );

  private readonly ICoreClientAPI _capi;
  private readonly IClientNetworkChannel _channel;
  private readonly HandAssemblyStepper _stepper = new();
  private readonly HandAssemblyHold _hold = new();
  private readonly HandAssemblyHudDialog _hud;
  private readonly int _highlightSlot = ExHighlightSlots.Reserve(
    "exlib:handassembly"
  );
  private readonly Matrixf _model = new();

  private IReadOnlyList<AssemblyCandidate> _candidates = [];
  private BlockSelection? _aim;
  private Block? _part;
  private HandAssemblySpec? _spec;
  private BlockPos? _takeOff;
  private string _target = "";
  private string _what = "";
  private bool _short;
  private bool _hudShown;
  private bool _highlighted;

  public HandAssemblyClient(ICoreClientAPI capi, IClientNetworkChannel channel) {
    _capi = capi;
    _channel = channel;
    _hud = new HandAssemblyHudDialog(capi);
    capi.Input.InWorldAction += OnAction;
    capi.Event.RegisterGameTickListener(_ => Refresh(), RecomputeMs);
    capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "handassembly");
    capi.Event.LeaveWorld += Clear;
  }

  public double RenderOrder => 0.5;

  public int RenderRange => 24;

  private AssemblyCandidate? Shown =>
    _stepper.Shown is { } code
      ? _candidates.FirstOrDefault(c => c.Block.Code.ToString() == code)
      : null;

  private IClientPlayer? Player => _capi.World.Player;

  private ICraftEffects? Effects =>
    Player?.Entity is { } entity ? new EntityCraftEffects(entity) : null;

  private void OnAction(
    EnumEntityAction action,
    bool on,
    ref EnumHandling handled
  ) {
    if (action != EnumEntityAction.InWorldRightMouseDown || Player == null)
      return;
    if (!on) {
      _hold.Release(Effects);
      return;
    }

    Refresh();
    bool sneak = Player.Entity.Controls.Sneak;
    if (_part != null && _aim != null) {
      handled = EnumHandling.PreventDefault;
      if (!sneak) {
        _stepper.Step();
        Rebuild();
      } else
        BeginFix();
    } else if (sneak && _takeOff != null) {
      handled = EnumHandling.PreventDefault;
      BeginTakeOff();
    }
  }

  private void BeginFix() {
    AssemblyCandidate? shown = Shown;
    if (shown == null || _spec == null || _part == null)
      return;
    if (_short) {
      ShowRefusal(_spec);
      return;
    }
    _hold.Begin(
      _target,
      _what,
      Player!.InventoryManager.ActiveHotbarSlotNumber,
      shown.Joints,
      ToolDefault(Player.Entity.LeftHandItemSlot)
    );
  }

  private void BeginTakeOff() {
    IBlockAccessor world = _capi.World.BlockAccessor;
    Block block = world.GetBlock(_takeOff);
    _hold.Begin(
      _target,
      _what,
      Player!.InventoryManager.ActiveHotbarSlotNumber,
      HandAssemblyCandidates.JointsMade(world, _takeOff!, block),
      ToolDefault(Player.InventoryManager.ActiveHotbarSlot)
    );
  }

  private void ShowRefusal(HandAssemblySpec spec) {
    IPlayer player = Player!;
    if (
      !HandAssemblyServer.HoldsTool(player.Entity.LeftHandItemSlot, spec.Tool)
    )
      Show(
        HandAssemblyServer.NeedsTool,
        HandAssemblyServer.ToolName(_capi.World, spec.Tool)
      );
    else
      Show(
        HandAssemblyServer.NeedsFasteners,
        (Shown!.Joints * spec.PerJoint).ToString(),
        HandAssemblyServer.FastenerName(_capi.World, spec.Fastener),
        ExCarried.Count(player, spec.Fastener).ToString()
      );
  }

  private void Show(string code, params string[] args) =>
    _capi.TriggerIngameError(
      this,
      code,
      Lang.Get("game:placefailure-" + code, args)
    );

  /// <summary>Shows a refusal the server sent.</summary>
  public void ShowResult(AssemblyResult result) =>
    Show(result.Code, result.Args);

  private static CraftToolDefault? ToolDefault(ItemSlot tool) =>
    CraftSequenceRegistry.Shared.ToolDefault(
      tool.Itemstack?.Collectible.Code.ToString()
    );

  private void Refresh() {
    _part = null;
    _spec = null;
    _aim = null;
    _takeOff = null;
    _candidates = [];
    IClientPlayer? player = Player;
    if (player?.Entity == null) {
      HideDisplay();
      return;
    }

    BlockSelection? selection = player.CurrentBlockSelection;
    IBlockAccessor accessor = _capi.World.BlockAccessor;
    ItemSlot main = player.InventoryManager.ActiveHotbarSlot;
    int slot = player.InventoryManager.ActiveHotbarSlotNumber;

    if (
      main.Itemstack?.Block is { } part
      && HandAssemblySpec.Of(part) is { } spec
    ) {
      _part = part;
      _spec = spec;
      if (selection != null) {
        _aim = selection;
        _candidates = HandAssemblyCandidates.Of(
          _capi.World,
          player,
          part,
          selection
        );
        HandAssemblyTarget target = HandAssemblyTarget.Resolve(
          accessor,
          selection,
          part
        );
        _target = $"{target.Cell}|{target.Anchor.Code}";
      }
    } else if (
        selection != null
        && HandAssemblySpec.Of(accessor.GetBlock(selection.Position)) is { } aimed
        && HandAssemblyServer.HoldsTool(main, aimed.Tool)
        && IsFixed(accessor, selection.Position)
      ) {
      _takeOff = selection.Position.Copy();
      _target = $"{TakeOffKey}|{_takeOff}";
    }

    _stepper.Retarget(
      _target,
      [.. _candidates.Select(c => c.Block.Code.ToString())]
    );
    _what = _takeOff != null ? TakeOffKey : _stepper.Shown ?? "";
    _short = _spec != null && Shown is { } shown && IsShort(player, shown);
    _hold.Keep(_target, _what, slot, Effects);
    if (_part == null || _aim == null)
      HideDisplay();
    else
      Rebuild();
  }

  private bool IsFixed(IBlockAccessor world, BlockPos pos) =>
    world.GetBlock(pos) is not BlockNetworkNode
    || BEBehaviorAssembled.At(world, pos) is { Fixed: true };

  private bool IsShort(IPlayer player, AssemblyCandidate shown) {
    if (HandAssemblyGate.IsCreative(player))
      return false;
    HandAssemblySpec spec = _spec!;
    return !HandAssemblyServer.HoldsTool(
        player.Entity.LeftHandItemSlot,
        spec.Tool
      )
      || ExCarried.Count(player, spec.Fastener) < shown.Joints * spec.PerJoint;
  }

  private void Rebuild() {
    AssemblyCandidate? shown = Shown;
    if (shown == null || _aim == null || _spec == null) {
      HideGhost();
      ShowHud(Lang.Get(ExlibLang.HandassemblyHudNone));
      return;
    }

    IBlockAccessor world = _capi.World.BlockAccessor;
    HandAssemblyTarget target = HandAssemblyTarget.Resolve(world, _aim, _part!);
    var joins = new List<(string, BlockFacing)>();
    var positions = new List<BlockPos>();
    var colors = new List<int>();
    foreach (BlockFacing arm in shown.Arms) {
      BlockPos across = target.Cell.AddCopy(arm);
      positions.Add(across);
      if (shown.OpenArms.Contains(arm)) {
        colors.Add(OpenColor);
        continue;
      }
      colors.Add(JoinedColor);
      joins.Add(
        (world.GetBlock(across).GetPlacedBlockName(_capi.World, across), arm)
      );
    }
    _capi.World.HighlightBlocks(Player, _highlightSlot, positions, colors);
    _highlighted = true;

    int cost = shown.Joints * _spec.PerJoint;
    ShowHud(
      HandAssemblyHud.Line(
        (key, args) => Lang.Get(key, args),
        _stepper.Index + 1,
        _stepper.Count,
        new ItemStack(shown.Block).GetName(),
        joins,
        shown.OpenArms,
        cost,
        HandAssemblyServer.FastenerName(_capi.World, _spec.Fastener),
        ExCarried.Count(Player!, _spec.Fastener)
      )
    );
  }

  private void ShowHud(string line) {
    _hud.Show(line);
    _hudShown = true;
  }

  private void HideGhost() {
    if (_highlighted && Player != null)
      _capi.World.HighlightBlocks(Player, _highlightSlot, [], []);
    _highlighted = false;
  }

  private void Clear() {
    _hold.Release(Effects);
    HideDisplay();
  }

  private void HideDisplay() {
    HideGhost();
    if (_hudShown)
      _hud.Hide();
    _hudShown = false;
  }

  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (Player?.Entity is not { } entity)
      return;
    if (_hold.Active) {
      if (!entity.Controls.RightMouseDown || !entity.Controls.Sneak)
        _hold.Release(Effects);
      else if (_hold.Advance(deltaTime, Effects))
        Send();
    }
    DrawGhost(entity);
  }

  private void Send() {
    if (_takeOff != null) {
      _channel.SendPacket(
        new AssemblyTakeOff {
          X = _takeOff.X,
          Y = _takeOff.Y,
          Z = _takeOff.Z,
          Dimension = _takeOff.dimension,
        }
      );
      return;
    }
    if (Shown is not { } shown || _aim == null || _part == null)
      return;
    _channel.SendPacket(
      new AssemblyFix {
        X = _aim.Position.X,
        Y = _aim.Position.Y,
        Z = _aim.Position.Z,
        Dimension = _aim.Position.dimension,
        Face = _aim.Face.Index,
        HitX = _aim.HitPosition.X,
        HitY = _aim.HitPosition.Y,
        HitZ = _aim.HitPosition.Z,
        Slot = Player!.InventoryManager.ActiveHotbarSlotNumber,
        PartCode = _part.Code.ToString(),
        CandidateCode = shown.Block.Code.ToString(),
      }
    );
  }

  private void DrawGhost(EntityPlayer entity) {
    if (Shown is not { } shown || _aim == null || _part == null)
      return;

    BlockPos cell = HandAssemblyTarget
      .Resolve(_capi.World.BlockAccessor, _aim, _part)
      .Cell;
    Vec3d camera = entity.CameraPos;
    float fill = BaseAlpha + ((1f - BaseAlpha) * _hold.Progress);
    IRenderAPI render = _capi.Render;
    render.GlToggleBlend(true);
    IStandardShaderProgram shader = render.PreparedStandardShader(
      cell.X,
      cell.Y,
      cell.Z
    );
    (float r, float g, float b) = _short ? ShortTint : GhostTint;
    shader.RgbaTint = new Vec4f(r, g, b, fill);
    shader.ModelMatrix = _model
      .Identity()
      .Translate(cell.X - camera.X, cell.Y - camera.Y, cell.Z - camera.Z)
      .Values;
    shader.ViewMatrix = render.CameraMatrixOriginf;
    shader.ProjectionMatrix = render.CurrentProjectionMatrix;
    render.RenderMultiTextureMesh(
      _capi.TesselatorManager.GetDefaultBlockMeshRef(shown.Block),
      "tex"
    );
    shader.Stop();
  }

  public void Dispose() {
    _capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
    _hud.Dispose();
  }
}
