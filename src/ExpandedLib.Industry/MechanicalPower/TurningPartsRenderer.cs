using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// Draws every <see cref="TurningInstance"/> of a client, one instanced draw per shape, part and texture set, at
/// the angle of its run. The Before stage advances the client's <see cref="MpRunAngles"/>.
/// </summary>
internal sealed class TurningPartsRenderer : IRenderer {
  private const string CacheKey = "exlib:turningparts";
  private const int StartCapacity = 64;
  private const int LightsPerFrame = 64;
  private const int Floats = 20;

  private readonly ICoreClientAPI _capi;
  private readonly Dictionary<string, Group> _groups = [];
  private IShaderProgram? _program;

  private TurningPartsRenderer(ICoreClientAPI capi) {
    _capi = capi;
    capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "exlibturning");
    capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "exlibturning");
  }

  public double RenderOrder => 0.5;

  public int RenderRange => 100;

  internal IReadOnlyCollection<Group> Groups => _groups.Values;

  /// <summary>The renderer of one client, made and registered on first use and kept in the client's object cache.</summary>
  internal static TurningPartsRenderer For(ICoreClientAPI capi) =>
    ObjectCacheUtil.GetOrCreate(
      capi,
      CacheKey,
      () => new TurningPartsRenderer(capi)
    );

  internal void Add(TurningInstance instance) {
    string key = instance.GroupKey;
    if (!_groups.TryGetValue(key, out Group? group))
      _groups[key] = group = new Group(key, instance);
    group.Instances.Add(instance);
    group.Template ??= instance;
  }

  internal void Remove(TurningInstance instance) {
    foreach (var (key, group) in _groups)
      if (group.Instances.Remove(instance)) {
        if (group.Instances.Count == 0) {
          group.Dispose();
          _groups.Remove(key);
        } else if (ReferenceEquals(group.Template, instance))
          group.Template = group.Instances[0];
        return;
      }
  }

  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (stage == EnumRenderStage.Before) {
      MpRunAngles.For(_capi).Advance(deltaTime, _capi.ElapsedMilliseconds);
      return;
    }
    foreach (Group group in _groups.Values)
      group.Prepare(_capi);
    if (_capi.World.Player?.Entity?.CameraPos is not { } camera)
      return;
    if (_program == null || _program.Disposed)
      _program = _capi.Shader.GetProgramByName("instanced");
    _capi.Render.GlDisableCullFace();
    _capi.Render.GlToggleBlend(false);
    _program.Use();
    _program.BindTexture2D(
      "tex",
      _capi.BlockTextureAtlas.Positions.FirstOrDefault()?.atlasTextureId ?? 0,
      0
    );
    _program.Uniform("rgbaFogIn", _capi.Render.FogColor);
    _program.Uniform("rgbaAmbientIn", _capi.Render.AmbientColor);
    _program.Uniform("fogMinIn", _capi.Render.FogMin);
    _program.Uniform("fogDensityIn", _capi.Render.FogDensity);
    _program.UniformMatrix(
      "projectionMatrix",
      _capi.Render.CurrentProjectionMatrix
    );
    _program.UniformMatrix("modelViewMatrix", _capi.Render.CameraMatrixOriginf);
    foreach (Group group in _groups.Values)
      group.Draw(_capi, camera);
    _program.Stop();
    _capi.Render.GlEnableCullFace();
  }

  public void Dispose() {
    foreach (Group group in _groups.Values)
      group.Dispose();
    _groups.Clear();
    _capi.ObjectCache.Remove(CacheKey);
  }

  /// <summary>The instance buffer size, in instances, that holds <paramref name="count"/> instances: <paramref name="capacity"/> doubled until it does.</summary>
  internal static int CapacityFor(int count, int capacity) {
    while (capacity < count)
      capacity *= 2;
    return capacity;
  }

  /// <summary>
  /// The instance matrix: the cell's offset from the camera, the block's rotation about the cell centre, then
  /// <paramref name="theta"/> about <paramref name="axis"/> through <paramref name="pivot"/>.
  /// </summary>
  /// <param name="offset">Cell corner minus camera, in blocks.</param>
  /// <param name="blockRotationDeg">The block shape's rotateX, rotateY and rotateZ in degrees.</param>
  /// <param name="part">The part, for its axis and pivot.</param>
  /// <param name="theta">Turn in rad.</param>
  internal static float[] Compose(
    Vec3f offset,
    Vec3f blockRotationDeg,
    TurningPart part,
    double theta
  ) {
    float[] m = Mat4f.Create();
    Mat4f.Translate(m, m, offset.X + 0.5f, offset.Y + 0.5f, offset.Z + 0.5f);
    Mat4f.RotateX(m, m, blockRotationDeg.X * GameMath.DEG2RAD);
    Mat4f.RotateY(m, m, blockRotationDeg.Y * GameMath.DEG2RAD);
    Mat4f.RotateZ(m, m, blockRotationDeg.Z * GameMath.DEG2RAD);
    Vec3f pivot = part.Pivot ?? new Vec3f(8f, 8f, 8f);
    Mat4f.Translate(
      m,
      m,
      pivot.X / 16f - 0.5f,
      pivot.Y / 16f - 0.5f,
      pivot.Z / 16f - 0.5f
    );
    float turn = (float)theta;
    switch (part.Axis) {
      case EnumAxis.X:
        Mat4f.RotateX(m, m, turn);
        break;
      case EnumAxis.Y:
        Mat4f.RotateY(m, m, turn);
        break;
      default:
        Mat4f.RotateZ(m, m, turn);
        break;
    }
    Mat4f.Translate(m, m, -pivot.X / 16f, -pivot.Y / 16f, -pivot.Z / 16f);
    return m;
  }

  /// <summary>The instances that share one mesh and so one draw.</summary>
  internal sealed class Group(string key, TurningInstance template) {
    private MeshData? _mesh;
    private MeshRef? _ref;
    private float[] _values = new float[Floats * StartCapacity];
    private readonly MeshData _update = new();
    private int _lightCursor;
    private bool _failed;

    public string Key => key;

    public List<TurningInstance> Instances { get; } = [];

    /// <summary>An instance of the group the mesh is built from; any will do.</summary>
    public TurningInstance? Template { get; set; } = template;

    /// <summary>Instances the instance buffer holds.</summary>
    public int Capacity { get; private set; } = StartCapacity;

    /// <summary>Builds and uploads the mesh, and grows the instance buffer, when the group needs it.</summary>
    internal void Prepare(ICoreClientAPI capi) {
      if (!_failed)
        EnsureMesh(capi);
    }

    internal void Draw(ICoreClientAPI capi, Vec3d camera) {
      if (_ref == null || _mesh == null)
        return;
      RefreshLights(capi);
      int i = 0;
      foreach (TurningInstance instance in Instances) {
        BlockPos pos = instance.Pos;
        Block block = instance.Owner.Blockentity.Block;
        float[] m = Compose(
          new Vec3f(
            (float)(pos.X - camera.X),
            (float)(pos.InternalY - camera.Y),
            (float)(pos.Z - camera.Z)
          ),
          new Vec3f(
            block.Shape.rotateX,
            block.Shape.rotateY,
            block.Shape.rotateZ
          ),
          instance.Part,
          instance.Theta
        );
        int j = i * Floats;
        _values[j] = instance.Light.R;
        _values[j + 1] = instance.Light.G;
        _values[j + 2] = instance.Light.B;
        _values[j + 3] = instance.Light.A;
        Array.Copy(m, 0, _values, j + 4, 16);
        i++;
      }
      _mesh.CustomFloats.Values = _values;
      _mesh.CustomFloats.Count = i * Floats;
      _update.CustomFloats = _mesh.CustomFloats;
      capi.Render.UpdateMesh(_ref, _update);
      capi.Render.RenderMeshInstanced(_ref, i);
    }

    private bool EnsureMesh(ICoreClientAPI capi) {
      int wanted = CapacityFor(Instances.Count, Capacity);
      if (_ref != null && wanted == Capacity)
        return true;
      Capacity = wanted;
      _values = new float[Floats * Capacity];
      _ref?.Dispose();
      _ref = null;
      _mesh ??= BuildMesh(capi);
      if (_mesh == null) {
        _failed = true;
        return false;
      }
      _mesh.CustomFloats = new CustomMeshDataPartFloat(Floats * Capacity) {
        Instanced = true,
        InterleaveOffsets = [0, 16, 32, 48, 64],
        InterleaveSizes = [4, 4, 4, 4, 4],
        InterleaveStride = 16 + 4 * 16,
        StaticDraw = false,
      };
      _mesh.CustomFloats.SetAllocationSize(Floats * Capacity);
      _ref = capi.Render.UploadMesh(_mesh);
      return true;
    }

    private MeshData? BuildMesh(ICoreClientAPI capi) {
      TurningInstance instance = Template!;
      Block block = instance.Owner.Blockentity.Block;
      if (
        ExMeshCache.LoadShape(capi, ExMeshCache.ShapePathOf(block))
        is not { } shape
      )
        return null;
      string[] patterns = instance.Part.Elements;
      Shape turning = ExShapeElements.Pruned(shape, patterns);
      if (
        ExShapeElements
          .AllPaths(ExShapeElements.Without(shape, patterns))
          .Count() == ExShapeElements.AllPaths(shape).Count()
      )
        capi.Logger.Error(
          "Turning part {0} of block {1} matches no element of its shape.",
          string.Join(",", patterns),
          block.Code
        );
      capi.Tesselator.TesselateShape(block, turning, out MeshData mesh);
      return mesh;
    }

    private void RefreshLights(ICoreClientAPI capi) {
      int count = Math.Min(LightsPerFrame, Instances.Count);
      for (int n = 0; n < count; n++) {
        _lightCursor = (_lightCursor + 1) % Instances.Count;
        TurningInstance instance = Instances[_lightCursor];
        instance.Light = capi.World.BlockAccessor.GetLightRGBs(
          instance.Pos.X,
          instance.Pos.Y,
          instance.Pos.Z
        );
      }
    }

    internal void Dispose() {
      _ref?.Dispose();
      _ref = null;
    }
  }
}
