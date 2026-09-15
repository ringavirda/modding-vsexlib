using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>Shared base for a mechanical-power node whose block renders a static body plus a
/// vanilla-spun axle.</summary>
public abstract class BEBehaviorMPSubmachineBase(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity) {
  /// <summary>The network-discovery face in the placed orientation.</summary>
  protected abstract BlockFacing ResolveDiscoveryFace();

  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = ResolveDiscoveryFace();

    // One sign per axis, not per facing: opposite facings on an axis share an axle line.
    AxisSign =
      OutFacingForNetworkDiscovery.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  protected override CompositeShape GetShape() =>
    new() {
      Base = Block.Shape.Base.Clone(),
      SelectiveElements = ["Axle*"],
      rotateY = Block.Shape.rotateY,
      InsertBakedTextures = true,
    };

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) {
    // Keyed on the block code alone: the body mesh is a pure function of the blocktype's shape.
    if (
      Api is ICoreClientAPI capi
      && ExMeshCache.GetOrCreate(
        capi,
        Block,
        "body",
        () => BuildBody(tesselator)
      )
        is { } body
    )
      mesher.AddMeshData(body);

    base.OnTesselation(mesher, tesselator);
    return true;
  }

  private MeshData? BuildBody(ITesselatorAPI tesselator) {
    if (
      ExMeshCache.LoadShape(Api, ExMeshCache.ShapePathOf(Block))
      is not { } shape
    )
      return null;

    // Render the whole body except the Axle* elements; vanilla's MP renderer spins those.
    Shape body = shape.Clone();
    body.Elements = body
      .Elements.Where(e => !e.Name?.StartsWith("Axle") ?? true)
      .ToArray();
    tesselator.TesselateShape(Block, body, out MeshData mesh);
    // Inside the factory, not on the way out: the cached mesh is shared and Rotate mutates in place.
    ExMesh.RotateByShape(mesh, Block);
    return mesh;
  }
}
