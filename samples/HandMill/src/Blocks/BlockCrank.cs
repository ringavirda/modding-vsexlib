using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using HandMill.BlockEntities;
using Vintagestory.API.Common;

namespace HandMill.Blocks;

/// <summary>
/// A hand crank: a fixed endpoint of the mpenergy network. Sneak-click winds it (nothing to
/// sneak-click here - a plain click drives it), feeding <see cref="BlockEntityCrank.Wind"/> a burst
/// of drive torque that eases off as the run spins up.
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
  /// compass mapping.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "crank")
        .Class<BlockCrank>()
        .EntityClass<BlockEntityCrank>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(64)
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
  /// A fixed endpoint has no neighbour to connect toward, so its orientation is the player's own look
  /// direction - the same token <see cref="ExpandedLib.Blocks.BlockBehaviorExOrientable"/>'s
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

  /// <summary>The crank declares no "type" variant group, so <c>Type</c> is always null and the base
  /// default (keyed by type) never resolves; a fixed endpoint has one canonical facing regardless.</summary>
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
