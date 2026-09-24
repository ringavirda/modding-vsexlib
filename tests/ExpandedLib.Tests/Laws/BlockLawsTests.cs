using System;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The block laws against small blocks that each break one law: a placement that writes an
/// undeclared side, a megablock that clears its fillers only on a player break, a multiblock read in
/// a frame it does not complete in, a layout naming a block nobody registers, and a construction
/// that refunds its stages and drops itself.</summary>
public class BlockLawsTests {
  private static readonly FillerCellSpec[] TwoCells =
  [
    new(-1, 0, 0),
    new(1, 0, 0),
  ];

  private static readonly System.Reflection.Assembly[] Exlib =
  [
    typeof(BlockStructureFiller).Assembly,
  ];

  private static void Prepare(TestWorld world) {
    world.RegisterClass("test-breakonly", typeof(BreakOnlyCleanup));
    world.RegisterClass("test-dropsfiller", typeof(DropsAFiller));
    world.RegisterClass("test-sideframe", typeof(SideFrame));
    world.RegisterClass("test-turnedframe", typeof(TurnedFrame));
    world.RegisterClass("test-plain", typeof(PlainBe));
  }

  private static TestWorld Stand(params ExBlockDef[] defs) =>
    BlockLaws.Stand(defs, Exlib, Prepare);

  private static ExBlockDef Turns(string code, params string[] sides) =>
    ExBlockDef
      .Create("test", code)
      .VariantGroup("side", sides)
      .Behavior("HorizontalOrientable");

  private static ExBlockDef Mega(string code, string cls) =>
    ExBlockDef
      .Create("test", code)
      .Class(cls)
      .SideVariant()
      .FillerOffsets(TwoCells);

  private static ExBlockDef Structure(
    string code,
    string entity,
    string wall = "test:wall"
  ) =>
    ExBlockDef
      .Create("test", code)
      .SideVariant()
      .EntityClass(entity)
      .Multiblock(m =>
        m.Number($"test:{code}-*", 1)
          .Number(wall, 2)
          .Number("game:rock-granite", 3)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
          .At(0, 0, 1, 3)
          .At(-1, 1, 2, 2)
      );

  private static readonly ExBlockDef Wall = ExBlockDef.Create("test", "wall");

  #region Placement

  // Fails when a throwing TryPlaceBlock is not recorded, or a stand or face is skipped.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void A_side_the_blocktype_does_not_declare_is_named_from_where_it_was_placed() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(
        Turns("halfturn", "north", "east"),
        Turns("turns", "north", "east", "south", "west")
      ),
      "test"
    );

    Assert.Equal(2, law.Blocks);
    Assert.Equal(2 * 36, law.Cases);
    Assert.All(
      law.Findings,
      f =>
        Assert.Matches(
          "^test:halfturn-north placed from .* face threw NullReferenceException: "
            + "Unable to to find a rotated block with code test:halfturn-(south|west),",
          f
        )
    );
    Assert.Equal(
      6,
      law.Findings.Count(f =>
        f.StartsWith(
          "test:halfturn-north placed from the north",
          StringComparison.Ordinal
        ) && f.Contains("code test:halfturn-south,", StringComparison.Ordinal)
      )
    );
    Assert.Equal(
      6,
      law.Findings.Count(f =>
        f.StartsWith(
          "test:halfturn-north placed from the east",
          StringComparison.Ordinal
        ) && f.Contains("code test:halfturn-west,", StringComparison.Ordinal)
      )
    );
  }

  #endregion

  #region Megablock

  // Fails when the law removes the principal by a player break, which clears the fillers through
  // OnBlockBroken, instead of through the accessor's SetBlock.
  [Fact]
  [PlantedDefect(typeof(MegablockLaw), nameof(MegablockLaw.Run))]
  public void A_megablock_clearing_its_fillers_only_on_a_break_leaves_them_on_removal() {
    BlockLaws.Law law = MegablockLaw.Run(
      Stand(
        Mega("breakonly", "test-breakonly"),
        Mega("whole", "ExFilledMegastructure")
      ),
      "test"
    );

    Assert.Equal(2, law.Blocks);
    Assert.Equal(8, law.Cases);
    Assert.Equal(
      [
        .. new[] { "e", "n", "s", "w" }.Select(side =>
          $"test:breakonly-{side} removed by the world left 2 filler cells standing"
        ),
      ],
      law.Findings.Order(StringComparer.Ordinal)
    );
  }

  #endregion

  #region Multiblock

  // Fails when PeripheralCell stops reading GetGlobalPos, or the law stops comparing it with the
  // cell the completion check reads.
  [Fact]
  [PlantedDefect(typeof(MultiblockLaw), nameof(MultiblockLaw.Run))]
  public void A_multiblock_read_in_another_frame_than_it_completes_in_is_named() {
    BlockLaws.Law law = MultiblockLaw.Run(
      Stand(
        Wall,
        Structure("turned", "test-turnedframe"),
        Structure("framed", "test-sideframe")
      ),
      "test"
    );

    Assert.Equal(2, law.Blocks);
    Assert.Equal(8, law.Cases);
    Assert.Equal(4, law.Findings.Count);
    Assert.All(
      law.Findings,
      f =>
        Assert.Matches(
          @"^test:turned-[nesw] reads 3 layout cell\(s\) as peripherals elsewhere than it "
            + @"completes them: \(1, 0, 0\) at ",
          f
        )
    );
  }

  // Fails when FillReal places a stand-in, or any block, for a cell no registered block matches.
  [Fact]
  [PlantedDefect(typeof(MultiblockLaw), nameof(MultiblockLaw.Run))]
  public void A_layout_cell_no_registered_block_satisfies_is_named() {
    BlockLaws.Law law = MultiblockLaw.Run(
      Stand(Wall, Structure("unmade", "test-sideframe", "test:nosuchwall")),
      "test"
    );

    Assert.Contains(
      "test:unmade-n wants test:nosuchwall at (1, 0, 0), which no registered block satisfies",
      law.Findings
    );
    Assert.Contains(
      "test:unmade-n does not complete at angle 0: 2 cell(s) unsatisfied",
      law.Findings
    );
  }

  // Fails when the law fills a family cell with a stand-in: the wall cells would then not hold
  // test:wall, or when the monitor tick is not driven.
  [Fact]
  public void A_multiblock_completes_on_registered_family_blocks_in_every_facing() {
    TestWorld world = Stand(Wall, Structure("framed", "test-sideframe"));

    BlockLaws.Law law = MultiblockLaw.Run(world, "test");

    Assert.Empty(law.Findings);
    Assert.Equal(4, law.Cases);
    SideFrame[] frames =
    [
      .. world.BlockEntities.Select(e => e.Value).OfType<SideFrame>(),
    ];
    Assert.Equal(4, frames.Length);
    Assert.All(
      frames,
      frame => {
        Assert.True(frame.StructureComplete);
        Assert.Equal(
          ["test:wall", "test:wall"],
          frame
            .LayoutCells.Where(c => c.Wanted.Path == "wall")
            .Select(c => world.GetBlock(c.At).Code.ToString())
        );
      }
    );
  }

  #endregion

#if GAME_GE_1_22
  #region Break

  // Fails when the law skips a stage-paid break's own-code check.
  [Fact]
  [PlantedDefect(typeof(BreakLaw), nameof(BreakLaw.Run))]
  public void A_construction_that_refunds_its_stages_and_drops_itself_is_named()
  {
    BlockLaws.Law law = BreakLaw.Run(
      [
        ExBlockDef
          .Create("test", "selfdrop")
          .Class("ExFilledMegastructure")
          .FillerOffsets(TwoCells)
          .EntityClass("test-plain")
          .Construction(c =>
            c.Stage(s => s.Require("game:stick", 1))
              .Stage(s => s.Require("game:plank-oak", 3))
          ),
      ],
      Exlib,
      Prepare
    );

    Assert.Contains(
      "test:selfdrop broken at stage 1 refunds its paid stages and also drops its own code",
      law.Findings
    );
  }

  // Fails when the law skips the filler-drop check.
  [Fact]
  [PlantedDefect(typeof(BreakLaw), nameof(BreakLaw.Run))]
  public void A_break_that_drops_a_structure_filler_is_named()
  {
    BlockLaws.Law law = BreakLaw.Run(
      [Mega("fillerdrop", "test-dropsfiller")],
      Exlib,
      Prepare
    );

    Assert.Contains(
      "test:fillerdrop-n broken from the principal drops a structure filler",
      law.Findings
    );
  }

  #endregion
#endif

  #region Every law

  // Fails when Run drops a law, or hands a law another domain's blocks.
  [Fact]
  [PlantedDefect(typeof(BlockLaws), nameof(BlockLaws.Run))]
  public void Run_reports_each_law_over_the_blocks_of_its_domain() {
    BlockLaws.Result result = BlockLaws.Run(
      "test",
      [
        Wall,
        Turns("halfturn", "north", "east"),
        Mega("breakonly", "test-breakonly"),
        Structure("turned", "test-turnedframe"),
        ExBlockDef
          .Create("other", "halfturn")
          .VariantGroup("side", "north")
          .Behavior("HorizontalOrientable"),
      ],
      Exlib,
      Prepare
    );

    Assert.Contains(
      result[PlacementLaw.Name].Findings,
      f => f.StartsWith("test:halfturn-north", StringComparison.Ordinal)
    );
    Assert.DoesNotContain(
      result[PlacementLaw.Name].Findings,
      f => f.StartsWith("other:", StringComparison.Ordinal)
    );
    Assert.Contains(
      result[MegablockLaw.Name].Findings,
      f => f.StartsWith("test:breakonly-n", StringComparison.Ordinal)
    );
    Assert.Contains(
      result[MultiblockLaw.Name].Findings,
      f => f.StartsWith("test:turned-n", StringComparison.Ordinal)
    );
#if GAME_GE_1_22
    Assert.Equal(
      ["placement", "break", "multiblock", "megablock"],
      result.Laws.Select(l => l.Name)
    );
#else
    Assert.Equal(
      ["placement", "multiblock", "megablock"],
      result.Laws.Select(l => l.Name)
    );
#endif
  }

  #endregion

  #region Fixtures

  private sealed class BreakOnlyCleanup : BlockFilledMegastructure {
    public override void OnBlockBroken(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) {
      StructureFillers.RemoveFillers(
        world,
        pos,
        StructureFillers.FootprintCells(
          this,
          pos,
          ExOrientation.AngleFromSide(Variant["side"])
        )
      );
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) =>
      world.BlockAccessor.RemoveBlockEntity(pos);
  }

  private sealed class DropsAFiller : BlockFilledMegastructure {
    public override void OnBlockBroken(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) {
      world.SpawnItemEntity(
        new ItemStack(world.GetBlock(StructureFillers.FillerCode)),
        pos.ToVec3d()
      );
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
  }

  private class SideFrame : BlockEntityMultiblockStructure {
    protected virtual int Offset => 0;

    protected override void UpdateStructureRotation() {
      if (Block != null)
        SetStructureAngle(
          ExOrientation.AngleFromSide(Block.Variant["side"]),
          Offset
        );
    }

    protected override string GetIncompleteMessage(int missingCount) =>
      $"missing {missingCount}";

    protected override string GetCompleteMessage() => "complete";
  }

  private sealed class TurnedFrame : SideFrame {
    protected override int Offset => 180;
  }

  private sealed class PlainBe : BlockEntity { }

  #endregion
}
