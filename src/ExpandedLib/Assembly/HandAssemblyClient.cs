using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The client's side of hand assembly: hands the right button and each frame to a <see cref="HandAssemblyControl"/>, and shows the ghost and the line of the placement or the cap it shows.</summary>
internal sealed class HandAssemblyClient : IRenderer {
  private const int RecomputeMs = 100;

  private readonly ICoreClientAPI _capi;
  private readonly HandAssemblyControl _control;
  private readonly HandAssemblyHudDialog _hud;
  private readonly HandAssemblyCapGhost _capGhost = new();
  private readonly Matrixf _model = new();

  private bool _hudShown;
  private readonly MeshRef?[] _rings = new MeshRef?[6];
  private LoadedTexture? _white;

  public HandAssemblyClient(ICoreClientAPI capi, IClientNetworkChannel channel) {
    _capi = capi;
    _control = new HandAssemblyControl(channel, Show);
    _hud = new HandAssemblyHudDialog(capi);
    capi.Input.InWorldAction += OnAction;
    capi.Event.RegisterGameTickListener(_ => Refresh(), RecomputeMs);
    capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "handassembly");
    capi.Event.LeaveWorld += Clear;
  }

  public double RenderOrder => 0.5;

  public int RenderRange => 24;

  private IClientPlayer? Player => _capi.World.Player;

  private void OnAction(
    EnumEntityAction action,
    bool on,
    ref EnumHandling handled
  ) {
    if (action != EnumEntityAction.InWorldRightMouseDown || Player == null)
      return;
    if (!on) {
      _control.Release();
      return;
    }

    if (
      _control.Press(
        _capi.World,
        Player,
        Player.CurrentBlockSelection,
        Player.Entity.Controls.Sneak
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

  /// <summary>Shows what the server answered: the line of a take-off it did, or a refusal.</summary>
  public void ShowResult(AssemblyResult result) {
    if (result.Done)
      _capi.TriggerIngameDiscovery(
        this,
        result.Code,
        Lang.Get("exlib:handassembly-hud-" + result.Code, result.Args)
      );
    else
      Show(result.Code, result.Args);
  }

  private void Refresh() {
    IClientPlayer? player = Player;
    _control.Refresh(_capi.World, player, player?.CurrentBlockSelection);
    Display();
  }

  private void Display() {
    if (_control.CapFace != null)
      RebuildCap();
    else if (
      _control.TakeOffLine(
        _capi.World,
        Player,
        (key, args) => Lang.Get(key, args)
      ) is { } takeOff
    )
      ShowHud(takeOff);
    else if (_control.Part == null || _control.Aim == null)
      HideDisplay();
    else
      Rebuild();
  }

  private void RebuildCap() {
    HandAssemblySpec spec = _control.CapSpec!;
    ItemStack? cap = Player?.InventoryManager.ActiveHotbarSlot.Itemstack;
    ShowHud(
      HandAssemblyHud.CapLine(
        (key, args) => Lang.Get(key, args),
        cap?.GetName() ?? "",
        _control.CapRefusal is { } refusal
          ? HandAssemblyServer.Line(null, refusal)
          : null,
        spec.PerJoint,
        HandAssemblyServer.FastenerName(_capi.World, spec.Fastener),
        ExCarried.Count(Player!, spec.Fastener)
      )
    );
  }

  private void Rebuild() {
    AssemblyCandidate? shown = _control.Shown;
    BlockSelection? aim = _control.Aim;
    HandAssemblySpec? spec = _control.Spec;
    if (shown == null || aim == null || spec == null) {
      ShowHud(
        _control.Refusal is { } refusal
          ? HandAssemblyServer.Line(null, refusal)
          : Lang.Get(ExlibLang.HandassemblyHudNone)
      );
      return;
    }

    IBlockAccessor world = _capi.World.BlockAccessor;
    HandAssemblyTarget target = HandAssemblyTarget.Resolve(
      world,
      aim,
      _control.Part!
    );
    var joins = new List<(string, BlockFacing)>();
    foreach (BlockFacing arm in shown.Arms) {
      if (shown.OpenArms.Contains(arm))
        continue;
      BlockPos across = target.Cell.AddCopy(arm);
      joins.Add(
        (world.GetBlock(across).GetPlacedBlockName(_capi.World, across), arm)
      );
    }

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

  private void Clear() {
    _control.Release();
    HideDisplay();
  }

  private void HideDisplay() {
    if (_hudShown)
      _hud.Hide();
    _hudShown = false;
  }

  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (Player?.Entity is not { } entity)
      return;
    _control.Frame(_capi.Input.InWorldMouseButton.Right);
    if (_control.CapFace != null)
      DrawCapGhost(entity);
    else
      DrawGhost(entity);
  }

  /// <summary>The colour of the ghost: red when <paramref name="isShort"/>, else <paramref name="blockColor"/> at the projection's alpha.</summary>
  /// <param name="blockColor">The shown block's colour as <c>Block.GetColor</c> returns it.</param>
  /// <param name="isShort">True when the tool or the fasteners fall short, <see cref="HandAssemblyControl.Short"/>.</param>
  internal static int GhostColor(int blockColor, bool isShort) =>
    isShort ? ProjectionColors.Refused : ProjectionColors.Of(blockColor);

  /// <summary>Whether a cap ghost is drawn: only for an end that takes a cap, so one whose refusal is not the cost shows none.</summary>
  /// <param name="capRefusal">The aimed end's refusal, <see cref="HandAssemblyControl.CapRefusal"/>; null when it takes a cap.</param>
  internal static bool ShowsCapGhost(string? capRefusal) => capRefusal == null;

  private void DrawCapGhost(EntityPlayer entity) {
    if (
      !ShowsCapGhost(_control.CapRefusal)
      || _control.CapAt is not { } cell
      || _control.CapFace is not { } face
      || _capGhost.Of(_capi, _capi.World.BlockAccessor.GetBlock(cell), face)
        is not { } ghost
    )
      return;
    Block piece = _capi.World.BlockAccessor.GetBlock(cell);
    Vec3d camera = entity.CameraPos;
    IShaderProgram? previous = _capi.Render.CurrentActiveShader;
    previous?.Stop();
    IStandardShaderProgram shader = Begin(cell);
    DrawProjected(
      shader,
      ghost,
      GhostColor(piece.GetColor(_capi, cell), _control.Short),
      cell.X - camera.X,
      cell.Y - camera.Y,
      cell.Z - camera.Z
    );
    shader.Stop();
    previous?.Use();
  }

  private LoadedTexture White() {
    if (_white == null) {
      _white = new LoadedTexture(_capi) { Width = 1, Height = 1 };
      _capi.Render.LoadOrUpdateTextureFromRgba(
        [ColorUtil.WhiteArgb],
        false,
        0,
        ref _white
      );
    }
    return _white;
  }

  private IStandardShaderProgram Begin(BlockPos cell) {
    IRenderAPI render = _capi.Render;
    IStandardShaderProgram shader = render.PreparedStandardShader(
      cell.X,
      cell.Y,
      cell.Z
    );
    shader.ViewMatrix = render.CameraMatrixOriginf;
    shader.ProjectionMatrix = render.CurrentProjectionMatrix;
    shader.RgbaLightIn = new Vec4f(1, 1, 1, 1);
    shader.RgbaAmbientIn = new Vec3f(1, 1, 1);
    shader.AlphaTest = 0.05f;
    return shader;
  }

  private void DrawProjected(
    IStandardShaderProgram shader,
    MultiTextureMeshRef mesh,
    int color,
    double x,
    double y,
    double z
  ) {
    IRenderAPI render = _capi.Render;
    shader.NormalShaded = 1;
    shader.RgbaTint = ProjectionColors.Tint(color);
    shader.ModelMatrix = _model.Identity().Translate(x, y, z).Values;
    render.GlToggleBlend(true);
    render.GLDepthMask(false);
    foreach (MeshRef part in mesh.meshrefs) {
      shader.BindTexture2D("tex", White().TextureId, 0);
      render.RenderMesh(part);
    }
    render.GLDepthMask(true);
    render.GlToggleBlend(false);
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
    double x = cell.X - camera.X;
    double y = cell.Y - camera.Y;
    double z = cell.Z - camera.Z;
    IShaderProgram? previous = _capi.Render.CurrentActiveShader;
    previous?.Stop();
    IStandardShaderProgram shader = Begin(cell);

    DrawMarks(shader, shown, x, y, z);
    DrawProjected(
      shader,
      _capi.TesselatorManager.GetDefaultBlockMeshRef(shown.Block),
      GhostColor(shown.Block.GetColor(_capi, cell), _control.Short),
      x,
      y,
      z
    );

    shader.Stop();
    previous?.Use();
  }

  /// <summary>Draws the see-through ring at each arm end of <paramref name="shown"/>, with the ghost's cell at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) from the camera.</summary>
  private void DrawMarks(
    IStandardShaderProgram shader,
    AssemblyCandidate shown,
    double x,
    double y,
    double z
  ) {
    IRenderAPI render = _capi.Render;
    shader.NormalShaded = 0;
    shader.BindTexture2D("tex", White().TextureId, 0);
    shader.ModelMatrix = _model.Identity().Translate(x, y, z).Values;
    render.GlDisableCullFace();
    render.GlToggleBlend(true);
    render.GLDepthMask(false);
    foreach (BlockFacing arm in shown.Arms) {
      _rings[arm.Index] ??= render.UploadMesh(HandAssemblyMarks.Ring(arm));
      shader.RgbaTint = HandAssemblyMarks.Color(shown.OpenArms.Contains(arm));
      render.RenderMesh(_rings[arm.Index]);
    }
    render.GLDepthMask(true);
    render.GlToggleBlend(false);
    render.GlEnableCullFace();
  }

  public void Dispose() {
    _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    foreach (MeshRef? ring in _rings)
      ring?.Dispose();
    _white?.Dispose();
    _capGhost.Dispose();
    _hud.Dispose();
  }
}
