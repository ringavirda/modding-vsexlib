using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>Draws a strung belt's <see cref="BeltMesh"/> static in the driving pulley's cell; registered and disposed by its owner.</summary>
public class BeltRenderer : IRenderer {
  private readonly ICoreClientAPI _api;
  private readonly BlockPos _pos;
  private readonly AssetLocation _texture;
  private readonly Matrixf _modelMat = new();
  private MeshRef? _meshRef;

  /// <summary>Render order within the stage.</summary>
  public double RenderOrder => 0.5;

  /// <summary>Distance in blocks within which the belt is drawn.</summary>
  public int RenderRange => 24;

  /// <param name="api">The client API.</param>
  /// <param name="pos">The driving pulley's cell, the origin of the mesh.</param>
  /// <param name="texture">The strap's texture asset, as <see cref="IRenderAPI.GetOrLoadTexture(AssetLocation)"/> takes it; the caller picks it.</param>
  public BeltRenderer(ICoreClientAPI api, BlockPos pos, AssetLocation texture) {
    _api = api;
    _pos = pos;
    _texture = texture;
  }

  /// <summary>Uploads <paramref name="mesh"/> in place of the last one; call once per belt change. A <c>null</c> mesh draws nothing. Runs on the client main thread, as it uploads to the GPU.</summary>
  /// <param name="mesh">A mesh from <see cref="BeltMesh.Build"/>; the renderer does not keep it.</param>
  public void SetMesh(MeshData? mesh) {
    _meshRef?.Dispose();
    _meshRef = mesh == null ? null : _api.Render.UploadMesh(mesh);
  }

  /// <summary>Draws the belt when a mesh is set.</summary>
  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (_meshRef == null)
      return;

    IRenderAPI render = _api.Render;
    int texId = render.GetOrLoadTexture(
      _texture
        .Clone()
        .WithPathPrefixOnce("textures/")
        .WithPathAppendixOnce(".png")
    );
    if (texId == 0)
      return;

    Vec3d camPos = _api.World.Player.Entity.CameraPos;

    render.GlDisableCullFace();

    IStandardShaderProgram shader = render.StandardShader;
    shader.Use();

    shader.RgbaAmbientIn = render.AmbientColor;
    shader.RgbaFogIn = render.FogColor;
    shader.FogMinIn = render.FogMin;
    shader.FogDensityIn = render.FogDensity;
    shader.DontWarpVertices = 0;
    shader.AddRenderFlags = 0;
    shader.ExtraGodray = 0f;
    shader.NormalShaded = 0;
    shader.RgbaLightIn = _api.World.BlockAccessor.GetLightRGBs(
      _pos.X,
      _pos.Y,
      _pos.Z
    );
    render.BindTexture2d(texId);

    shader.ModelMatrix = _modelMat
      .Identity()
      .Translate(_pos.X - camPos.X, _pos.Y - camPos.Y, _pos.Z - camPos.Z)
      .Values;
    shader.ViewMatrix = render.CameraMatrixOriginf;
    shader.ProjectionMatrix = render.CurrentProjectionMatrix;

    render.RenderMesh(_meshRef);
    shader.Stop();
    render.GlEnableCullFace();
  }

  /// <summary>Unregisters from the Opaque stage, where the owner registers it, and frees the mesh.</summary>
  public void Dispose() {
    _api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    _meshRef?.Dispose();
    _meshRef = null;
  }
}
