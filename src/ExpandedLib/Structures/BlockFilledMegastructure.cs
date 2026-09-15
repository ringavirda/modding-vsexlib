using System.Collections.Generic;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>
/// Shared base for a mega-block that occupies one grid cell but renders across a multi-cell footprint
/// reserved with invisible <see cref="BlockStructureFiller"/> cells.
/// </summary>
[BlockRegister("ExFilledMegastructure", PrefixModId = false)]
public class BlockFilledMegastructure : Block, IFillerHost {
  /// <summary>The block's <c>fillerOffsets</c> attribute, or null if none.</summary>
  public virtual JsonObject? FillerOffsets => Attributes?["fillerOffsets"];

  /// <summary>Rotation applied to the north-orientation footprint offsets to reach the placed orientation.</summary>
  public virtual int StructureAngle =>
    ExOrientation.AngleFromSide(Variant?["side"] ?? Variant?["orientation"]);

  public override void OnLoaded(ICoreAPI api) {
    base.OnLoaded(api);
    JsonMultiblockLayout.Resolve(this, api.Logger);
  }

  /// <summary>The block's world footprint cells for a principal at <paramref name="pos"/>.</summary>
  protected List<FillerCell> FootprintCells(BlockPos pos) =>
    StructureFillers.FootprintCells((IFillerHost)this, pos, StructureAngle);

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // The whole footprint must be clear for the fillers to spawn.
    if (!StructureFillers.CanPlace(world, FootprintCells(blockSel.Position))) {
      failureCode = "notenoughspace";
      return false;
    }
    return true;
  }

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  ) {
    base.OnBlockPlaced(world, blockPos, byItemStack);
    StructureFillers.PlaceFillers(world, blockPos, FootprintCells(blockPos));
    OnFootprintPlaced(world, blockPos);
  }

  /// <summary>Hook run right after the footprint fillers are placed. Default: nothing.</summary>
  protected virtual void OnFootprintPlaced(
    IWorldAccessor world,
    BlockPos blockPos
  ) { }

  public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) {
    // Called on every removal path, unlike OnBlockBroken, which only a player break triggers.
    StructureFillers.RemoveFillers(world, pos, FootprintCells(pos));
    base.OnBlockRemoved(world, pos);
  }
}
