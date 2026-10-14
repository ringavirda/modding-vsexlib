using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Structures;

/// <summary>
/// Shared base for a mega-block that occupies one grid cell but renders across a multi-cell footprint
/// reserved with invisible <see cref="BlockStructureFiller"/> cells.
/// </summary>
[BlockRegister("ExFilledMegastructure", PrefixModId = false)]
public class BlockFilledMegastructure
  : Block,
#if GAME_GE_1_22
    ICustomSelectionBoxRender,
#endif
    IFillerHost {
  /// <summary>The block's <c>fillerOffsets</c> attribute, or null if none.</summary>
  public virtual JsonObject? FillerOffsets => Attributes?["fillerOffsets"];

  /// <summary>Rotation applied to the north-orientation footprint offsets to reach the placed orientation.</summary>
  public virtual int StructureAngle =>
    ExOrientation.AngleFromSide(Variant?["side"] ?? Variant?["orientation"]);

  public override void OnLoaded(ICoreAPI api) {
    base.OnLoaded(api);
    JsonMultiblockLayout.Resolve(this, api.Logger);
  }

  public override Cuboidf[] GetCollisionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    ControlBoxes.Collision(this, blockAccessor, pos)
    ?? base.GetCollisionBoxes(blockAccessor, pos);

#if GAME_GE_1_22
  public override Cuboidf[] GetSelectionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    ControlBoxes.Selection(this, blockAccessor, pos)
    ?? base.GetSelectionBoxes(blockAccessor, pos);
#endif

#if GAME_GE_1_22
  /// <summary>Answers <see cref="ICustomSelectionBoxRender"/> only when the block has a <c>controls</c>
  /// table, so a block without one is outlined as vanilla outlines it. Available on 1.22 only.</summary>
  public override T GetInterface<T>(IWorldAccessor world, BlockPos pos) =>
    typeof(T) == typeof(ICustomSelectionBoxRender)
    && BlockControls.Of(this).Count == 0
      ? null!
      : base.GetInterface<T>(world, pos);

  /// <summary>Outlines the hit control whole through <see cref="ControlOutline"/>.</summary>
  public void RenderSelectionBoxes(
    BlockSelection blockSel,
    RenderBoxDelegate renderBoxHandler
  ) => ControlOutline.Render(this, api, blockSel, renderBoxHandler);
#endif

  /// <summary>The block's world footprint cells for a principal at <paramref name="pos"/>.</summary>
  protected List<FillerCell> FootprintCells(BlockPos pos) =>
    StructureFillers.FootprintCells((IFillerHost)this, pos, StructureAngle);

  /// <summary>The footprint cells a principal at <paramref name="pos"/> takes in
  /// <paramref name="world"/>: the whole footprint unless overridden. <see cref="HasRoom"/> checks
  /// them, <see cref="OnBlockPlaced"/> fills them, and <see cref="OnBlockRemoved"/> clears those of them
  /// still linked to this principal.</summary>
  protected virtual List<FillerCell> ReservedCells(
    IWorldAccessor world,
    BlockPos pos
  ) => FootprintCells(pos);

  /// <summary><see cref="ReservedCells"/> for the harness that places this block by the world.</summary>
  internal List<FillerCell> ReservedCellsAt(
    IWorldAccessor world,
    BlockPos pos
  ) => ReservedCells(world, pos);

  /// <summary>Whether a principal at <paramref name="pos"/> finds room for its fillers: by default,
  /// every <see cref="ReservedCells"/> cell is air or replaceable by the filler.</summary>
  /// <returns>False makes <see cref="CanPlaceBlock"/> refuse with <c>notenoughspace</c>.</returns>
  protected virtual bool HasRoom(IWorldAccessor world, BlockPos pos) =>
    StructureFillers.CanPlace(world, ReservedCells(world, pos));

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // The whole footprint must be clear for the fillers to spawn.
    if (!HasRoom(world, blockSel.Position)) {
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
    StructureFillers.PlaceFillers(
      world,
      blockPos,
      ReservedCells(world, blockPos)
    );
    OnFootprintPlaced(world, blockPos);
  }

  /// <summary>Hook run right after the footprint fillers are placed. Default: nothing.</summary>
  protected virtual void OnFootprintPlaced(
    IWorldAccessor world,
    BlockPos blockPos
  ) { }

  public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) {
    // Called on every removal path, unlike OnBlockBroken, which only a player break triggers.
    StructureFillers.RemoveFillers(world, pos, ReservedCells(world, pos));
    base.OnBlockRemoved(world, pos);
  }
}
