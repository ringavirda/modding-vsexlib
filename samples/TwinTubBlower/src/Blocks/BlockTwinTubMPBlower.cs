using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using TwinTubBlower.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TwinTubBlower.Blocks;

/// <summary>
/// The twin-tub blower: a mechanically driven pair of bellows that produces into the gas-pipe network it
/// stands in, needing no fuel and no player-fed material. It is both a gas-pipe node (it sits in the main
/// and produces into it, hence the <see cref="BlockPipe"/> base) and a mega-block reserving a 1x2x3
/// footprint of invisible fillers, whose upper-rear cell hosts a mechanical-power port so an axle on that
/// face drives the bellows. <see cref="BlockEntityTwinTubMPBlower"/> holds the simulation.
/// <para>
/// <see cref="BlockFilledMegastructure"/> is a plain <c>Block</c> and the blower must be a pipe, so this
/// type implements <see cref="IFillerHost"/> and drives the <see cref="StructureFillers"/> statics from
/// the placement triad below. Without those calls no fillers spawn and the MP port cell never exists.
/// </para>
/// </summary>
[BlockRegister]
public partial class BlockTwinTubMPBlower
  : BlockPipe,
    IExBlockDefProvider,
    IFillerHost {
  #region Code-first definition

  /// <summary>
  /// The mechanical-power intake hosted by the footprint's upper-rear cell: an axle on the
  /// (rotation-relative) west face drives the bellows.
  /// </summary>
  private static readonly FillerBehaviorSpec MpPortWest =
    FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>("west");

  /// <summary>
  /// The two cells the -Z run crosses before it reaches the principal's own pipe connector: a
  /// membership on north, passing through to south, so a run coupled two cells out still reaches the
  /// principal rather than stopping at whichever cell it first touches.
  /// </summary>
  private static readonly FillerBehaviorSpec PipeThrough =
    FillerBehaviorSpec.Of<BEBehaviorNetworkMember>(
      "north",
      new { networkType = "pipe", passThrough = true }
    );

  public static new IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "blower", "furnace/twintubblower")
        .Class<BlockTwinTubMPBlower>()
        .EntityClass<BlockEntityTwinTubMPBlower>()
        .Behavior("MultiblockStructure")
        .Material(EnumBlockMaterial.Ceramic)
        .MaxStackSize(1)
        .VariantGroup("type", "twintubblower")
        .VariantGroup("orientation", "n", "e", "s", "w")
        .NetworkOriented()
        .ShapeByType("*-n", "twintubblower:furnace/twintubmpblower", rotateY: 0)
        .ShapeByType(
          "*-e",
          "twintubblower:furnace/twintubmpblower",
          rotateY: 90
        )
        .ShapeByType(
          "*-s",
          "twintubblower:furnace/twintubmpblower",
          rotateY: 180
        )
        .ShapeByType(
          "*-w",
          "twintubblower:furnace/twintubmpblower",
          rotateY: 270
        )
        .CreativeCommon("*-n")
        .FillerOffsets(
          StructureFootprint.Layout(f =>
            f.Host('M', MpPortWest)
              .Host('p', PipeThrough)
              .Slab('_', BlockFacing.DOWN)
              .Origin(-2, 1)
              .Slice(
                0,
                """
                _ # M
                p p 0
                """
              )
          )
        )
        .SolidNonOpaque(),
    ];

  #endregion

  #region Footprint

  /// <summary>The block's <c>fillerOffsets</c> attribute (from the injected code-first def).</summary>
  public JsonObject? FillerOffsets => Attributes?["fillerOffsets"];

  /// <summary>
  /// Rotation applied to the north-frame footprint to reach the placed orientation. Read from the
  /// pipe-fitting <c>orientation</c> variant (n 0, e 90, s 180, w 270), matching the per-orientation
  /// shape rotations in the definition above. The block entity rotates its MP-port lookup by the same
  /// angle, so the two can never disagree.
  /// </summary>
  public int StructureAngle =>
    Variant?["orientation"] switch {
      "e" => 90,
      "s" => 180,
      "w" => 270,
      _ => 0,
    };

  private List<FillerCell> FootprintCells(BlockPos pos) =>
    StructureFillers.FootprintCells(this, pos, StructureAngle);

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // Refuse placement unless the whole volume is clear, else the fillers fail to spawn and the blower
    // would stand with no MP port cell to be driven through.
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
  }

  public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) {
    // Runs on every removal path (a player break, an explosion, a worldedit delete), unlike
    // OnBlockBroken, so the reserved volume is never left behind.
    StructureFillers.RemoveFillers(world, pos, FootprintCells(pos));
    base.OnBlockRemoved(world, pos);
  }

  #endregion
}
