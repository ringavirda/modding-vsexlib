using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// Turns parts of a block's shape on the angle of the "mpshaft" run that holds its nodes: every cell of a run
/// shows the same angle, whichever cells were loaded first. The parts are drawn by one instanced renderer per
/// client, the rest of the shape goes to the chunk mesh, and clips read <see cref="AngleRad"/>.
/// </summary>
/// <remarks>
/// Declared as <c>parts</c> in the behavior's properties, each an object of <c>elements</c> (shape element paths),
/// <c>slot</c> (default 0), <c>ratio</c> (default 1), <c>sign</c> (+1 or -1, default +1), <c>axis</c> (<c>x</c>,
/// <c>y</c> or <c>z</c>, default <c>z</c>) and <c>pivot</c> (voxels in shape space, default [8, 8, 8]). With no
/// <c>parts</c> the whole shape turns about z through the cell centre; <c>"parts": []</c> turns nothing, for a
/// block whose clips only read <see cref="AngleRad"/>. A part whose paths match no element is logged once and
/// stays in the chunk mesh. The run of each slot is written to the synced tree by the network when the layout of
/// the run changes.
/// </remarks>
[BlockEntityBehaviorRegister]
public class BEBehaviorTurningParts(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) {
  private const string RunKey = "turnRun";

  private IReadOnlyList<TurningPart> _parts = [new TurningPart(["*"])];
  private readonly Dictionary<byte, (NodeKey Anchor, float Ratio)> _runs = [];
  private readonly List<TurningInstance> _instances = [];

  /// <summary>The parts this block turns.</summary>
  public IReadOnlyList<TurningPart> Parts => _parts;

  internal IReadOnlyList<TurningInstance> Instances => _instances;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (properties?["parts"] is { Exists: true } declared)
      _parts = [.. declared.AsArray().Select(Parse)];
    Register();
  }

  /// <summary>Replaces the parts with <paramref name="parts"/>: on a client the instances are made again and the cell is drawn again.</summary>
  /// <param name="parts">The parts to turn; empty for none.</param>
  public void UseParts(IReadOnlyList<TurningPart> parts) {
    _parts = parts;
    if (Api is ICoreClientAPI) {
      Register();
      Blockentity.MarkDirty(true);
    }
  }

  /// <summary>The angle of the run holding the node at <paramref name="slot"/> as this node turns, in rad, folded to [0, 2 pi).</summary>
  /// <param name="slot">The node's slot in this cell.</param>
  /// <returns>0 on the server, before the first packet that names the run, and for a slot with no run.</returns>
  public float AngleRad(byte slot) => (float)Wrap(NodeAngle(slot));

  internal double NodeAngle(byte slot) {
    if (
      Api is not ICoreClientAPI capi
      || !_runs.TryGetValue(slot, out var run)
      || !MpRunAngles.For(capi).TryGet(run.Anchor, out MpRunAngles.Run state)
    )
      return 0d;
    return state.Angle * run.Ratio;
  }

  internal static double Wrap(double rad) =>
    rad - Math.Floor(rad / (2 * Math.PI)) * 2 * Math.PI;

  /// <summary>Names the run of the node at <paramref name="slot"/>; marks the cell dirty only when the run or the ratio changed.</summary>
  internal void SetRun(byte slot, NodeKey anchor, float ratio) {
    if (
      _runs.TryGetValue(slot, out var known)
      && known.Anchor.Equals(anchor)
      && known.Ratio == ratio
    )
      return;
    _runs[slot] = (anchor, ratio);
    Blockentity.MarkDirty();
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    var runs = new TreeAttribute();
    foreach (var (slot, (anchor, ratio)) in _runs) {
      var run = new TreeAttribute();
      run.SetInt("x", anchor.Pos.X);
      run.SetInt("y", anchor.Pos.Y);
      run.SetInt("z", anchor.Pos.Z);
      run.SetInt("sl", anchor.Slot);
      run.SetFloat("r", ratio);
      runs["s" + slot] = run;
    }
    tree[RunKey] = runs;
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    _runs.Clear();
    if (tree[RunKey] is not ITreeAttribute runs)
      return;
    foreach (var (name, value) in runs) {
      if (value is not ITreeAttribute run || !name.StartsWith('s'))
        continue;
      _runs[byte.Parse(name[1..])] = (
        new NodeKey(
          new BlockPos(run.GetInt("x"), run.GetInt("y"), run.GetInt("z")),
          (byte)run.GetInt("sl")
        ),
        run.GetFloat("r")
      );
    }
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    Unregister();
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    Unregister();
  }

  /// <summary>Adds the shape without the turning elements to the chunk and skips the block's own mesh; a block with no parts keeps its own mesh.</summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) {
    if (Api is not ICoreClientAPI capi || _parts.Count == 0)
      return false;
    string[] turning = [.. _parts.SelectMany(p => p.Elements).Distinct()];
    if (
      ExMeshCache.GetOrCreate(
        capi,
        Block,
        "turningstill|" + string.Join(",", turning),
        () => BuildStill(tesselator, turning)
      ) is { } mesh
    )
      mesher.AddMeshData(mesh);
    return true;
  }

  private MeshData? BuildStill(ITesselatorAPI tesselator, string[] turning) {
    if (
      ExMeshCache.LoadShape(Api, ExMeshCache.ShapePathOf(Block))
      is not { } shape
    )
      return null;
    Shape still = ExShapeElements.Without(shape, turning);
    if (still.Elements.Length == 0)
      return null;
    tesselator.TesselateShape(
      Block,
      still,
      out MeshData mesh,
      new Vec3f(Block.Shape.rotateX, Block.Shape.rotateY, Block.Shape.rotateZ)
    );
    return mesh;
  }

  private void Register() {
    Unregister();
    if (Api is not ICoreClientAPI capi)
      return;
    TurningPartsRenderer renderer = TurningPartsRenderer.For(capi);
    foreach (TurningPart part in _parts) {
      var instance = new TurningInstance(this, part);
      _instances.Add(instance);
      renderer.Add(instance);
    }
  }

  private void Unregister() {
    if (Api is ICoreClientAPI capi) {
      TurningPartsRenderer renderer = TurningPartsRenderer.For(capi);
      foreach (TurningInstance instance in _instances)
        renderer.Remove(instance);
    }
    _instances.Clear();
  }

  private static TurningPart Parse(JsonObject part) =>
    new(
      part["elements"].AsArray<string>() ?? ["*"],
      (byte)part["slot"].AsInt(0),
      part["ratio"].AsFloat(1f),
      part["sign"].AsInt(1) < 0 ? -1 : 1,
      part["axis"].AsString("z").ToLowerInvariant() switch {
        "x" => EnumAxis.X,
        "y" => EnumAxis.Y,
        _ => EnumAxis.Z,
      },
      part["pivot"].AsArray<float>() is { Length: 3 } p
        ? new Vec3f(p[0], p[1], p[2])
        : null
    );
}

/// <summary>One part of one block as the renderer draws it.</summary>
internal sealed class TurningInstance(
  BEBehaviorTurningParts owner,
  TurningPart part
) {
  public BEBehaviorTurningParts Owner => owner;

  public TurningPart Part => part;

  public BlockPos Pos => owner.Blockentity.Pos;

  /// <summary>Light at the cell, refreshed by the renderer a few instances a frame.</summary>
  public Vec4f Light { get; set; } = new(1f, 1f, 1f, 1f);

  /// <summary>The part's angle in rad in [0, 2 pi): the run's angle times the node's ratio, the part's ratio and its sign.</summary>
  public double Theta =>
    BEBehaviorTurningParts.Wrap(
      owner.NodeAngle(part.Slot) * part.Ratio * part.Sign
    );

  /// <summary>What instances sharing a mesh share: the block's shape, the parts' element paths and its textures.</summary>
  public string GroupKey {
    get {
      Block block = owner.Blockentity.Block;
      string textures =
        block.Textures == null
          ? ""
          : string.Join(
            ",",
            block
              .Textures.OrderBy(t => t.Key, StringComparer.Ordinal)
              .Select(t => t.Key + "=" + t.Value.Base)
          );
      return $"{block.Shape.Base}|{string.Join(",", part.Elements.OrderBy(e => e, StringComparer.Ordinal))}|{textures}";
    }
  }
}
