using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
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

/// <summary>The twin-tub blower: bellows producing into the gas-pipe network it stands in, as a
/// <see cref="BlockPipe"/> node with a 1x2x3 footprint of invisible fillers hosting the drive port.
/// Implements <see cref="IFillerHost"/> to spawn them via <see cref="StructureFillers"/>.</summary>
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
    FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>(
      "west",
      new { through = false }
    );

  /// <summary>The two pass-through filler cells on the -Z run: membership on north, passing
  /// through to south.</summary>
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
        .Behavior("BlockEntityInteract")
        .EntityBehavior("Animatable")
        .Material(EnumBlockMaterial.Ceramic)
        .MaxStackSize(1)
        .NoDrops()
        .VariantGroup("type", "twintubblower")
        .VariantGroup("orientation", "n", "e", "s", "w")
        .NetworkOriented()
        .ShapeByType("*-n", "twintubblower:furnace/twintubmpblower", rotateY: 0)
        .ShapeByType(
          "*-e",
          "twintubblower:furnace/twintubmpblower",
          rotateY: 270
        )
        .ShapeByType(
          "*-s",
          "twintubblower:furnace/twintubmpblower",
          rotateY: 180
        )
        .ShapeByType(
          "*-w",
          "twintubblower:furnace/twintubmpblower",
          rotateY: 90
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
        .Construction(c =>
          c.Stage(s => s.AddElements("Root/Base", "Root/BaseExtention"))
            .Stage(s =>
              s.Require(
                  "game:supportbeam-*",
                  4,
                  "twintubblower:rcc-ingredient-beam",
                  type: "block"
                )
                .RequireMetalNails(domain, 2)
                .AddElements("Root/BaseBeam")
            )
            .Stage(s =>
              s.Require("game:plank-*", 4, "twintubblower:rcc-ingredient-plank")
                .Require(
                  "game:woodenaxle-ud",
                  1,
                  "twintubblower:rcc-ingredient-axle",
                  type: "block"
                )
                .RequireMetalRod(domain, 2)
                .AddElements("Root/AxleGear")
            )
            .Stage(s =>
              s.RequireMetalPlate(domain, 4)
                .Require(
                  "game:plank-*",
                  4,
                  "twintubblower:rcc-ingredient-plank"
                )
                .RequireMetalNails(domain, 4)
                .AddElements("Root/Tubs")
            )
            .Stage(s =>
              s.RequireMetalPlate(domain, 4).AddElements("Root/PipeConn")
            )
        )
        .ShapeSelectiveElements("Root/Base/*")
        .SolidNonOpaque(),
    ];

  #endregion

  #region Drops

  // Returns construction materials via RightClickConstructable, never the block itself.
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [];

  #endregion

  #region Footprint

  /// <summary>The block's <c>fillerOffsets</c> attribute (from the injected code-first def).</summary>
  public JsonObject? FillerOffsets => Attributes?["fillerOffsets"];

  /// <summary>Rotation applied to the north-frame footprint to reach the placed orientation,
  /// per <see cref="ExOrientation.AngleFromSide"/>.</summary>
  public int StructureAngle =>
    ExOrientation.AngleFromSide(Variant?["orientation"]);

  private List<FillerCell> FootprintCells(BlockPos pos) =>
    StructureFillers.FootprintCells(this, pos, StructureAngle);

  /// <summary>The direction the pipe-through fillers couple through: the footprint's north face,
  /// rotated by <see cref="StructureAngle"/>.</summary>
  public BlockFacing OutletFace =>
    ExOrientation.RotateFacing(BlockFacing.NORTH, StructureAngle);

  /// <summary>World cell of the far pipe-through filler, two cells out along the footprint's
  /// -Z run.</summary>
  public BlockPos OutletCell(BlockPos principal) =>
    ExOrientation.GlobalPos(principal, 0, 0, -2, StructureAngle);

  /// <summary>Answers a connector only on <see cref="OutletFace"/>.</summary>
  public override bool HasConnectorAt(BlockFacing face) => face == OutletFace;

  /// <summary>Places the blower facing the way the player is looking, and locks that facing at
  /// placement.</summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!world.BlockAccessor.GetBlock(blockSel.Position).IsReplacableBy(this)) {
      failureCode = "notreplaceable";
      return false;
    }

    string token = ExOrientation.TokenOf(
      SuggestedHVOrientation(byPlayer, blockSel)[0],
      asLetter: true
    );
    if (
      world.BlockAccessor.GetBlock(CodeWithVariant("orientation", token))
      is not BlockTwinTubMPBlower oriented
    )
      return false;

    if (!oriented.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    world.BlockAccessor.SetBlock(
      oriented.BlockId,
      blockSel.Position,
      itemstack
    );
    return true;
  }

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // Refuses placement unless the whole footprint volume is clear.
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
    // Runs on every removal path (break, explosion, worldedit delete), unlike OnBlockBroken.
    StructureFillers.RemoveFillers(world, pos, FootprintCells(pos));
    base.OnBlockRemoved(world, pos);
  }

  #endregion

  #region Orientation lock

  /// <summary>Pins this block's orientation to whatever it already wears; a neighbour's wrench
  /// rotation can never re-pick it.</summary>
  protected override string[] ComputeValidOrientations(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    string type,
    string? currentOrientation
  ) => Orientation != null ? [Orientation] : [];

  /// <summary>No-op: the blower keeps its placement facing; ignores the network's connector-scan
  /// orientation.</summary>
  public override void RecalculateAndSyncOrientations(
    IWorldAccessor world,
    BlockPos pos
  ) { }

  /// <summary>The blower is never wrench-rotatable: its footprint is fixed to the facing it was
  /// placed with.</summary>
  protected override bool CanWrenchRotate(IWorldAccessor world, BlockPos pos) =>
    false;

  #endregion
}
