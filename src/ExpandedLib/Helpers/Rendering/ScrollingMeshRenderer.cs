using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>The texture coordinate a <see cref="ScrollingMeshRenderer"/> scrolls.</summary>
public enum ScrollAxis {
  /// <summary>The first texture coordinate.</summary>
  U,

  /// <summary>The second texture coordinate.</summary>
  V,
}

/// <summary>
/// Draws a block entity's mesh with the standard shader, its own repeating texture (not an atlas tile, so UVs
/// count texture lengths and may run past 1) scrolling along one axis at <see cref="Speed"/>: a running belt.
/// Registered and disposed by its owner, on the client main thread.
/// </summary>
public class ScrollingMeshRenderer : IRenderer {
  private readonly ICoreClientAPI _api;
  private readonly BlockPos _pos;
  private readonly AssetLocation _texture;
  private readonly ScrollAxis _axis;
  private readonly MeshData _mesh;
  private readonly float[] _baseUv;
  private readonly MeshRef _meshRef;
  private readonly Matrixf _modelMat = new();
  private float _drawnPhase;

  /// <summary>Texture lengths per second along the scrolled axis; a positive speed moves the pattern
  /// toward the axis' increasing coordinate, a negative one back, 0 holds it. Settable every frame, as a
  /// belt follows its shaft: the scroll carries on from where it is.</summary>
  public float Speed;

  /// <summary>Whether the mesh is drawn; the scroll stands still while it is not.</summary>
  public bool Visible = true;

  /// <summary>The scroll's offset, in texture lengths, 0 inclusive to 1 exclusive.</summary>
  public float Phase { get; private set; }

  /// <summary>Render order within the stage.</summary>
  public double RenderOrder => 0.5;

  /// <summary>Distance in blocks within which the mesh is drawn.</summary>
  public int RenderRange => 24;

  /// <param name="api">The client API.</param>
  /// <param name="pos">The cell the mesh is drawn in and lit from; the mesh is in its 0-1 block space.</param>
  /// <param name="mesh">The mesh, with UVs in texture lengths and opaque white vertex colours; the renderer
  /// draws a copy and leaves this one as it is.</param>
  /// <param name="texture">The texture asset, as <see cref="IRenderAPI.GetOrLoadTexture(AssetLocation)"/>
  /// takes it without its <c>textures/</c> prefix and <c>.png</c>, e.g. <c>game:block/leather/plain</c>.</param>
  /// <param name="axis">The texture coordinate that scrolls.</param>
  public ScrollingMeshRenderer(
    ICoreClientAPI api,
    BlockPos pos,
    MeshData mesh,
    AssetLocation texture,
    ScrollAxis axis
  ) {
    _api = api;
    _pos = pos;
    _texture = texture
      .Clone()
      .WithPathPrefixOnce("textures/")
      .WithPathAppendixOnce(".png");
    _axis = axis;
    _mesh = mesh.Clone();
    _baseUv = (float[])_mesh.Uv.Clone();
    _meshRef = api.Render.UploadMesh(_mesh);
  }

  /// <summary>The scroll's offset after <paramref name="deltaTime"/> at <paramref name="speed"/>.</summary>
  /// <param name="phase">The offset now, in texture lengths.</param>
  /// <param name="speed">Texture lengths per second, either sign.</param>
  /// <param name="deltaTime">Seconds since <paramref name="phase"/>.</param>
  /// <returns>The new offset, wrapped to 0 inclusive to 1 exclusive.</returns>
  public static float Advance(float phase, float speed, float deltaTime) {
    float next = (phase + speed * deltaTime) % 1f;
    if (next < 0f)
      next += 1f;
    return next >= 1f ? 0f : next;
  }

  /// <summary>Writes the scrolled UVs: <paramref name="phase"/> taken off the scrolled coordinate of
  /// each vertex, the other coordinate copied.</summary>
  /// <param name="baseUv">The unscrolled UVs, two per vertex.</param>
  /// <param name="uv">The UVs to write, at least as long as <paramref name="baseUv"/>.</param>
  /// <param name="vertexCount">Vertices to write.</param>
  /// <param name="axis">The coordinate that scrolls.</param>
  /// <param name="phase">The offset in texture lengths.</param>
  public static void Offset(
    float[] baseUv,
    float[] uv,
    int vertexCount,
    ScrollAxis axis,
    float phase
  ) {
    int scrolled = axis == ScrollAxis.U ? 0 : 1;
    for (int i = 0; i < vertexCount; i++) {
      uv[i * 2] = baseUv[i * 2];
      uv[i * 2 + 1] = baseUv[i * 2 + 1];
      uv[i * 2 + scrolled] -= phase;
    }
  }

  /// <summary>Advances the scroll by the frame and draws the mesh while <see cref="Visible"/>.</summary>
  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (!Visible)
      return;

    IRenderAPI render = _api.Render;
    int texId = render.GetOrLoadTexture(_texture);
    if (texId == 0)
      return;

    Phase = Advance(Phase, Speed, deltaTime);
    if (Phase != _drawnPhase) {
      Offset(_baseUv, _mesh.Uv, _mesh.VerticesCount, _axis, Phase);
      render.UpdateMesh(_meshRef, _mesh);
      _drawnPhase = Phase;
    }

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
    _meshRef.Dispose();
  }
}
