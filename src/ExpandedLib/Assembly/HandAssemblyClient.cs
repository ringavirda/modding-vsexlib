using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The client's side of hand assembly: hands the right button and each frame to a <see cref="HandAssemblyControl"/>, and shows the ghost and the line of the placement it shows.</summary>
internal sealed class HandAssemblyClient : IRenderer {
  private const int RecomputeMs = 100;

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
  private readonly HandAssemblyControl _control;
  private readonly HandAssemblyHudDialog _hud;
  private readonly int _highlightSlot = ExHighlightSlots.Reserve(
    "exlib:handassembly"
  );
  private readonly Matrixf _model = new();

  private bool _hudShown;
  private bool _highlighted;

  public HandAssemblyClient(ICoreClientAPI capi, IClientNetworkChannel channel) {
    _capi = capi;
    _control = new HandAssemblyControl(channel, Show);
    _hud = new HandAssemblyHudDialog(capi);
    capi.Input.InWorldAction += OnAction;
    capi.Event.RegisterGameTickListener(_ => Refresh(), RecomputeMs);
    capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "handassembly");
    capi.Event.LeaveWorld += Clear;
  }

  public double RenderOrder => 0.5;

  public int RenderRange => 24;

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
      _control.Release(Effects);
      return;
    }

    if (
      _control.Press(
        _capi.World,
        Player,
        Player.CurrentBlockSelection,
        Player.Entity.Controls.Sneak,
        Effects
      )
    )
      handled = EnumHandling.PreventDefault;
    Display();
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

  private void Refresh() {
    IClientPlayer? player = Player;
    _control.Refresh(
      _capi.World,
      player,
      player?.CurrentBlockSelection,
      Effects
    );
    Display();
  }

  private void Display() {
    if (_control.Part == null || _control.Aim == null)
      HideDisplay();
    else
      Rebuild();
  }

  private void Rebuild() {
    AssemblyCandidate? shown = _control.Shown;
    BlockSelection? aim = _control.Aim;
    HandAssemblySpec? spec = _control.Spec;
    if (shown == null || aim == null || spec == null) {
      HideGhost();
      ShowHud(Lang.Get(ExlibLang.HandassemblyHudNone));
      return;
    }

    IBlockAccessor world = _capi.World.BlockAccessor;
    HandAssemblyTarget target = HandAssemblyTarget.Resolve(
      world,
      aim,
      _control.Part!
    );
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

    int cost = shown.Joints * spec.PerJoint;
    ShowHud(
      HandAssemblyHud.Line(
        (key, args) => Lang.Get(key, args),
        _control.Index + 1,
        _control.Count,
        new ItemStack(shown.Block).GetName(),
        joins,
        shown.OpenArms,
        cost,
        HandAssemblyServer.FastenerName(_capi.World, spec.Fastener),
        ExCarried.Count(Player!, spec.Fastener)
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
    _control.Release(Effects);
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
    _control.Frame(
      deltaTime,
      _capi.Input.InWorldMouseButton.Right,
      entity.Controls.Sneak,
      Effects
    );
    DrawGhost(entity);
  }

  private void DrawGhost(EntityPlayer entity) {
    if (
      _control.Shown is not { } shown
      || _control.Aim is not { } aim
      || _control.Part is not { } part
    )
      return;

    BlockPos cell = HandAssemblyTarget
      .Resolve(_capi.World.BlockAccessor, aim, part)
      .Cell;
    Vec3d camera = entity.CameraPos;
    float fill = BaseAlpha + ((1f - BaseAlpha) * _control.Progress);
    IRenderAPI render = _capi.Render;
    render.GlToggleBlend(true);
    IStandardShaderProgram shader = render.PreparedStandardShader(
      cell.X,
      cell.Y,
      cell.Z
    );
    (float r, float g, float b) = _control.Short ? ShortTint : GhostTint;
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
