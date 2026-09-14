using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using HandMill.BlockEntities;
using Vintagestory.API.Common;

namespace HandMill.Blocks;

/// <summary>
/// A hand crank: the mpenergy network's producer, driving it with a burst of torque on every click,
/// feeding <see cref="BlockEntityCrank.Wind"/>.
/// </summary>
[BlockRegister]
public partial class BlockCrank : BlockNetworkNode, IExBlockDefProvider {
  public override string NetworkType => "mpenergy";

  // Not IsNetworkEndPoint: BlockNetworkModSystem.CouplesFrom refuses a graph edge to or from an
  // end point on every side, which would leave a producer marked one permanently isolated from
  // the run it exists to drive. The crank's single connector letter already makes it a structural
  // dead end with no further help needed.

  /// <summary>The crank's own single-cell type; the connector is the one face the letter names, so
  /// <c>HasConnectorAt</c> holds through the base. Rotations match vanilla's own crank shape's
  /// compass mapping. The single-state <c>type</c> group carries no variation of its own - it
  /// exists so <c>NetworkNodeContractCheck</c> finds one, the way the shaft's does.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "drive", "drive/crank")
        .Class<BlockCrank>()
        .EntityClass<BlockEntityCrank>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(64)
        .VariantGroup("type", "crank")
        .VariantGroup("orientation", "n", "e", "s", "w")
        .NetworkOriented()
        .ShapeByType("*-n", "game:block/wood/mechanics/crank", rotateY: 270)
        .ShapeByType("*-e", "game:block/wood/mechanics/crank", rotateY: 180)
        .ShapeByType("*-s", "game:block/wood/mechanics/crank", rotateY: 90)
        .ShapeByType("*-w", "game:block/wood/mechanics/crank", rotateY: 0)
        .CreativeCommon("*-n")
        .SingleCollisionBox(0.1875f, 0f, 0.1875f, 0.8125f, 0.625f, 0.8125f)
        .SingleSelectionBox(0.1875f, 0f, 0.1875f, 0.8125f, 0.625f, 0.8125f)
        .SideSolid(false)
        .SideOpaque(false),
    ];

  /// <summary>
  /// The crank has no neighbour yet to take an orientation from, so it faces the player's own look
  /// direction instead - the same token <see cref="ExpandedLib.Blocks.BlockBehaviorExOrientable"/>'s
  /// horizontal mode gives a wall-facing block, not the network node's default (which would face
  /// opposite the clicked face, toward whatever triggered the placement).
  /// </summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    string token = ExOrientation.TokenOf(
      SuggestedHVOrientation(byPlayer, blockSel)[0],
      asLetter: true
    );
    Block? oriented = world.BlockAccessor.GetBlock(
      CodeWithVariant("orientation", token)
    );
    if (oriented == null)
      return base.TryPlaceBlock(
        world,
        byPlayer,
        itemstack,
        blockSel,
        ref failureCode
      );
    return oriented.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
  }

  /// <summary>The crank's <c>type</c> group carries one state, so it has one canonical facing
  /// regardless of <paramref name="type"/>.</summary>
  protected override string GetFallbackOrientation(string? type) => "n";

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityCrank crank
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);
    if (ExInteraction.Of(world, byPlayer, blockSel).IsClient)
      return true;
    crank.Wind(HandMillValues.WindSeconds);
    return true;
  }
}
