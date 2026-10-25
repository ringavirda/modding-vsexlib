using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Helpers;

/// <summary>Base for renderers that draw a falling column of liquid (a pour from a spout) inside or beside a block.</summary>
public abstract class StreamRenderer : IRenderer {
  private const float Voxel = 16f;
  private const float ScrollSecondsPerHeight = 1f;

  protected readonly ICoreClientAPI Api;
  protected readonly BlockPos Pos;

  private readonly MeshData _mesh;
  private readonly MeshRef _meshRef;
  private readonly float _top;
  private readonly float _bottom;

  /// <summary>The model matrix of the last frame drawn.</summary>
  public Matrixf ModelMat = new();

  /// <summary>Whether the column is drawn; pushed by the owning block entity.</summary>
  public bool Visible;

  /// <summary>Render order within the stage.</summary>
  public abstract double RenderOrder { get; }

  /// <summary>Distance in blocks within which the stream is drawn.</summary>
  public virtual int RenderRange => 24;

  /// <param name="lip">Centre of the column's top face in 0-16 voxel space of the block's north frame.</param>
  /// <param name="landingY">Voxel height of the column's bottom face.</param>
  /// <param name="width">Voxels along x in the north frame.</param>
  /// <param name="depth">Voxels along z in the north frame.</param>
  /// <param name="rotationY">The block's Y rotation in radians, as <see cref="SurfaceRenderer"/> takes it.</param>
  /// <param name="pos">The block position the renderer draws at and lights from.</param>
  /// <param name="api">The client API.</param>
  protected StreamRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Vec3f lip,
    float landingY,
    float width,
    float depth,
    float rotationY
  ) {
    Pos = pos;
    Api = api;

    Cuboidf box = StreamBox(lip, landingY, width, depth, rotationY);
    _top = box.Y2 / Voxel;
    _bottom = box.Y1 / Voxel;
    _mesh = BuildMesh(box);
    _meshRef = api.Render.UploadMesh(_mesh);
  }

  /// <summary>The box a stream draws, in 0-16 voxel space of the block, turned about the block's centre.</summary>
  /// <param name="lip">Centre of the column's top face in voxels of the north frame.</param>
  /// <param name="landingY">Voxel height of the column's bottom face.</param>
  /// <param name="width">Voxels along x in the north frame.</param>
  /// <param name="depth">Voxels along z in the north frame.</param>
  /// <param name="rotationY">The block's Y rotation in radians; quarter turns keep the result exact to float precision.</param>
  /// <returns>The axis-aligned box after the rotation.</returns>
  public static Cuboidf StreamBox(
    Vec3f lip,
    float landingY,
    float width,
    float depth,
    float rotationY
  ) =>
    new Cuboidf(
      lip.X - width / 2f,
      landingY,
      lip.Z - depth / 2f,
      lip.X + width / 2f,
      lip.Y,
      lip.Z + depth / 2f
    ).RotatedCopyRad(0f, rotationY, 0f, new Vec3d(8, 8, 8));

  // Four side faces; the top and bottom are never seen (the lip and the receiver's metal hide them).
  private static MeshData BuildMesh(Cuboidf box) {
    MeshData mesh = new(16, 24);
    float x1 = box.X1 / Voxel,
      x2 = box.X2 / Voxel,
      z1 = box.Z1 / Voxel,
      z2 = box.Z2 / Voxel,
      y1 = box.Y1 / Voxel,
      y2 = box.Y2 / Voxel;
    (float, float, float, float)[] sides =
    [
      (x1, z1, x2, z1),
      (x2, z1, x2, z2),
      (x2, z2, x1, z2),
      (x1, z2, x1, z1),
    ];
    foreach (var (ax, az, bx, bz) in sides) {
      int start = mesh.VerticesCount;
      mesh.AddVertex(ax, y2, az, 0f, 0f);
      mesh.AddVertex(bx, y2, bz, 1f, 0f);
      mesh.AddVertex(bx, y1, bz, 1f, 1f);
      mesh.AddVertex(ax, y1, az, 0f, 1f);
      mesh.AddIndex(start);
      mesh.AddIndex(start + 1);
      mesh.AddIndex(start + 2);
      mesh.AddIndex(start);
      mesh.AddIndex(start + 2);
      mesh.AddIndex(start + 3);
    }
    mesh.Rgba.Fill(byte.MaxValue);
    return mesh;
  }

  // Texture v runs one texture height per block of fall; the phase moves it downward.
  private void ScrollUv() {
    float phase = Api.ElapsedMilliseconds / 1000f / ScrollSecondsPerHeight % 1f;
    float height = _top - _bottom;
    for (int i = 0; i < _mesh.VerticesCount; i++) {
      bool atTop = i % 4 < 2;
      _mesh.Uv[i * 2 + 1] = (atTop ? 0f : height) - phase;
    }
    Api.Render.UpdateMesh(_meshRef, _mesh);
  }

  /// <summary>Scrolls the texture and draws the column while <see cref="Visible"/>.</summary>
  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (!Visible)
      return;

    IRenderAPI render = Api.Render;
    Vec3d camPos = Api.World.Player.Entity.CameraPos;

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
    shader.RgbaLightIn = Api.World.BlockAccessor.GetLightRGBs(
      Pos.X,
      Pos.Y,
      Pos.Z
    );

    ConfigureShader(shader, render);

    // A texture bind failure aborts the frame; nothing is drawn untextured.
    if (!BindStreamTexture(render)) {
      shader.Stop();
      render.GlEnableCullFace();
      return;
    }

    ScrollUv();

    shader.ModelMatrix = ModelMat
      .Identity()
      .Translate(Pos.X - camPos.X, Pos.Y - camPos.Y, Pos.Z - camPos.Z)
      .Values;
    shader.ViewMatrix = render.CameraMatrixOriginf;
    shader.ProjectionMatrix = render.CurrentProjectionMatrix;

    render.RenderMesh(_meshRef);
    shader.Stop();
    render.GlEnableCullFace();
  }

  /// <summary>Tint, glow and temp-glow setup for the liquid being poured.</summary>
  protected abstract void ConfigureShader(
    IStandardShaderProgram shader,
    IRenderAPI render
  );

  /// <summary>Binds the stream texture; returning false skips the draw for this frame.</summary>
  protected abstract bool BindStreamTexture(IRenderAPI render);

  /// <summary>Unregisters from the Opaque stage only, where the owner registers it, and frees the mesh.</summary>
  public virtual void Dispose() {
    Api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    _meshRef.Dispose();
  }
}
