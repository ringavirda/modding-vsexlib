using System;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// Draws a machine tool's mounted piece turning about its kind's <see cref="MachineSpindle"/>: the angle advances
/// while the machine cuts and holds otherwise. Registered and disposed by the machine tool, on the client main thread.
/// </summary>
internal sealed class MountedPieceRenderer : IRenderer {
  private const string RefGroup = "machinepiece";

  private readonly ICoreClientAPI _capi;
  private readonly BlockEntityMachineTool _owner;
  private readonly Matrixf _modelMat = new();
  private float _angleRad;

  internal MountedPieceRenderer(
    ICoreClientAPI capi,
    BlockEntityMachineTool owner
  ) {
    _capi = capi;
    _owner = owner;
  }

  public double RenderOrder => 0.5;

  public int RenderRange => 24;

  /// <summary>The turn after <paramref name="deltaTime"/>: advanced at the spindle's rate while cutting, held otherwise.</summary>
  /// <param name="angleRad">The turn now, radians.</param>
  /// <param name="degreesPerSecond">The spindle's rate, degrees a second.</param>
  /// <param name="deltaTime">Seconds since <paramref name="angleRad"/>.</param>
  /// <param name="cutting">Whether the machine is cutting.</param>
  /// <returns>The new turn, radians, 0 inclusive to a full turn exclusive.</returns>
  internal static float Step(
    float angleRad,
    float degreesPerSecond,
    float deltaTime,
    bool cutting
  ) =>
    cutting
      ? (angleRad + degreesPerSecond * GameMath.DEG2RAD * deltaTime)
        % GameMath.TWOPI
      : angleRad;

  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (
      _owner.MountedDraw() is not var (piece, anchor, spindle, rotation)
      || _capi.World.Player?.Entity?.CameraPos is not { } camera
    )
      return;
    _angleRad = Step(
      _angleRad,
      spindle.DegreesPerSecond,
      deltaTime,
      _owner.IsCutting
    );
    MultiTextureMeshRef meshRef = ExMeshCache.GetOrCreateRef(
      _capi,
      RefGroup,
      piece.Collectible.Code.ToString(),
      () =>
        MachineToolRenderer.PieceMesh(_capi, _capi.Tesselator, piece)
        ?? new MeshData()
    );
    BlockPos pos = _owner.Pos;
    IRenderAPI render = _capi.Render;
    render.GlDisableCullFace();
    IStandardShaderProgram shader = render.PreparedStandardShader(
      pos.X,
      pos.Y,
      pos.Z
    );
    shader.ModelMatrix = _modelMat
      .Identity()
      .Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z)
      .Mul(MachineAnchors.Compose(anchor, rotation, spindle, _angleRad))
      .Values;
    shader.ViewMatrix = render.CameraMatrixOriginf;
    shader.ProjectionMatrix = render.CurrentProjectionMatrix;
    render.RenderMultiTextureMesh(meshRef, "tex");
    shader.Stop();
    render.GlEnableCullFace();
  }

  public void Dispose() =>
    _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
}
