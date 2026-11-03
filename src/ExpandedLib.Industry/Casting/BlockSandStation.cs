using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Casting;

/// <summary>
/// A sand station block: routes a right-click to its <see cref="BlockEntitySandStation"/> and shakes out a
/// set casting when the block is broken. The station it is comes from the block's <c>sandStation.station</c>
/// attribute; the block declares a <c>BEBehaviorMoltenCell</c> for the cavity.
/// </summary>
[BlockRegister]
public class BlockSandStation : Block {
  /// <inheritdoc/>
  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    (
      world.BlockAccessor.GetBlockEntity(pos) as BlockEntitySandStation
    )?.ShakeOutOnBreak();
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  /// <inheritdoc/>
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntitySandStation station
      && station.OnInteract(byPlayer)
    )
      return true;
    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }
}
