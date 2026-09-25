using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The block laws against small blocks that each break one law: placements that land an
/// undeclared side, another block or none; megablocks that keep or misplace their fillers;
/// multiblocks read in another frame, turned to one angle or naming an unregistered block; a
/// construction that drops itself; info that throws once ticked or logs; and entities that forget
/// a count or change class over a reload.</summary>
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
    world.RegisterClass("test-fixedframe", typeof(FixedFrame));
    world.RegisterClass("test-northfillers", typeof(NorthFillers));
    world.RegisterClass("test-placesnorth", typeof(PlacesNorth));
    world.RegisterClass("test-shortfillers", typeof(ShortFillers));
    world.RegisterClass("test-plain", typeof(PlainBe));
    world.RegisterClass("test-placeswall", typeof(PlacesAWall));
    world.RegisterClass("test-placeskindb", typeof(PlacesKindB));
    world.RegisterClass("test-placesbare", typeof(PlacesTheBareTurner));
    world.RegisterClass("test-neverplaces", typeof(NeverPlaces));
    world.RegisterClass("test-tickthrows", typeof(ThrowsOnceTicked));
    world.RegisterClass("test-tickbreaks", typeof(TickBreaks));
    world.RegisterClass("test-infologs", typeof(LogsItsInfo));
    world.RegisterClass("test-forgets", typeof(ForgetsItsCount));
    world.RegisterClass("test-keeps", typeof(KeepsItsCount));
    world.RegisterClass("test-swaps", typeof(SwapsOnPlacement));
    world.RegisterClass("test-other", typeof(OtherBe));
    world.RegisterClass("test-swapsblock", typeof(ExchangesToSwapped));
    world.RegisterClass("test-dropsentity", typeof(DropsItsEntity));
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

  private static ExBlockDef Entity(string code, string entity) =>
    ExBlockDef.Create("test", code).EntityClass(entity);

  private static ExBlockDef Oriented(string code, string cls) =>
    ExBlockDef
      .Create("test", code)
      .Class(cls)
      .SideVariant()
      .Behavior("ExOrientable");

  #region Placement

  // Fails when the law stops comparing the tokens placements landed with those the held stack's
  // variants declare.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void A_blocktype_that_always_lands_its_first_side_is_named() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(Oriented("northonly", "test-placesnorth")),
      "test"
    );

    Assert.Equal(
      ["test:northonly-n lands no side 'e', 's', 'w' from any stand or face"],
      law.Findings
    );
  }

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

  // Fails when a placement landing a block of another blocktype passes as a landing.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void A_placement_that_lands_another_blocktype_is_named() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(Wall, Oriented("walls", "test-placeswall")),
      "test"
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(36, law.Findings.Count);
    Assert.Equal(
      "test:walls-n placed from the north against its north face landed test:wall",
      law.Findings[0]
    );
  }

  // Fails when a placement that changes a group placement does not write passes as a landing.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void A_placement_that_lands_another_state_of_a_kept_group_is_named() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(
        ExBlockDef
          .Create("test", "kinds")
          .Class("test-placeskindb")
          .VariantGroup("kind", "a", "b")
          .SideVariant()
          .Behavior("ExOrientable")
      ),
      "test"
    );

    Assert.Equal(37, law.Findings.Count);
    Assert.Equal(
      "test:kinds-a-n placed from the north against its north face landed test:kinds-b-n",
      law.Findings[0]
    );
    Assert.Equal(
      "test:kinds-b-n lands no side 'e', 's', 'w' from any stand or face",
      law.Findings[^1]
    );
  }

  // Fails when a landed block missing a group placement writes passes the token check.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void A_landed_block_without_the_placed_group_is_named() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(
        ExBlockDef
          .Create("test", "turner")
          .Class("test-placesbare")
          .VariantGroup("kind", "a")
          .SideVariant()
          .Behavior("ExOrientable"),
        ExBlockDef
          .Create("test", "turner", "turner/bare")
          .VariantGroup("kind", "a")
      ),
      "test"
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(36, law.Findings.Count);
    Assert.Equal(
      "test:turner-a-n placed from the north against its north face landed test:turner-a, "
        + "whose side '' its blocktype does not declare",
      law.Findings[0]
    );
  }

  // Fails when a stack that every stand and face refuses is not named, or its refusals are not.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void A_stack_that_lands_from_no_stand_is_named_with_its_refusals() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(Oriented("never", "test-neverplaces")),
      "test"
    );

    Assert.Equal(36, law.Cases);
    Assert.Equal(
      [
        "test:never-n lands from no stand against no face (refused: test-never)",
      ],
      law.Findings
    );
  }

  // Fails when blocks sharing a code are judged as one blocktype whatever their variant groups.
  [Fact]
  [PlantedDefect(typeof(PlacementLaw), nameof(PlacementLaw.Run))]
  public void Definitions_sharing_a_code_are_judged_by_their_own_groups() {
    BlockLaws.Law law = PlacementLaw.Run(
      Stand(
        ExBlockDef
          .Create("test", "machine", "machine/press")
          .VariantGroup("type", "press")
          .SideVariant()
          .Behavior("ExOrientable"),
        ExBlockDef
          .Create("test", "machine", "machine/lathe")
          .VariantGroup("type", "lathe")
          .VariantGroup("axis", "ns", "we")
      ),
      "test"
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(36, law.Cases);
    Assert.Empty(law.Findings);
  }

  #endregion

  #region Megablock

  // Fails when the law removes the principal by a player break instead of the accessor's SetBlock.
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

  // Fails when the law accepts fillers turned to any facing instead of the variant's own
  // StructureAngle.
  [Fact]
  [PlantedDefect(typeof(MegablockLaw), nameof(MegablockLaw.Run))]
  public void A_megablock_raising_its_north_footprint_on_every_side_is_named() {
    BlockLaws.Law law = MegablockLaw.Run(
      Stand(Mega("northern", "test-northfillers")),
      "test"
    );

    Assert.Equal(4, law.Cases);
    Assert.Equal(
      ["e", "w"],
      law.Findings.Select(f => f.Split(' ')[0][^1..])
        .Order(StringComparer.Ordinal)
    );
    Assert.All(
      law.Findings,
      f =>
        Assert.Matches(
          @"^test:northern-[ew] placed raised fillers elsewhere than its footprint turned to "
            + @"(90|270) puts them: ",
          f
        )
    );
  }

  // Fails when the law stops comparing the filler count with the footprint's.
  [Fact]
  [PlantedDefect(typeof(MegablockLaw), nameof(MegablockLaw.Run))]
  public void A_megablock_raising_part_of_its_footprint_is_named() {
    BlockLaws.Law law = MegablockLaw.Run(
      Stand(Mega("short", "test-shortfillers")),
      "test"
    );

    Assert.Equal(
      [
        .. new[] { "e", "n", "s", "w" }.Select(side =>
          $"test:short-{side} placed raised 1 of its 2 filler cells"
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

  // Fails when the law rigs each variant at the angle its anchor turns to instead of the one its
  // side gives.
  [Fact]
  [PlantedDefect(typeof(MultiblockLaw), nameof(MultiblockLaw.Run))]
  public void A_multiblock_turning_every_facing_to_one_angle_is_named() {
    BlockLaws.Law law = MultiblockLaw.Run(
      Stand(Wall, Structure("fixed", "test-fixedframe")),
      "test"
    );

    Assert.Equal(4, law.Cases);
    Assert.Equal(3, law.Findings.Count);
    Assert.All(
      law.Findings,
      f =>
        Assert.Matches(
          @"^test:fixed-[nesw] turns its layout to 0, not the (90|180|270) its side '[nesw]' "
            + @"gives at the offset test:fixed-[nesw] turns by$",
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

  #region Info

  // Fails when the law skips the tick, ticks every listener instead of the case's, or stops
  // reading the info after it.
  [Fact]
  [PlantedDefect(typeof(InfoLaw), nameof(InfoLaw.Run))]
  public void Info_that_throws_only_once_ticked_is_named() {
    BlockLaws.Law law = InfoLaw.Run(
      Stand(Wall, Entity("tickthrows", "test-tickthrows")),
      "test"
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(1, law.Cases);
    Assert.Equal(
      "test:tickthrows info ticked threw InvalidOperationException: ticked 5 time(s)",
      Assert.Single(law.Findings).Split(" (")[0]
    );
  }

  // Fails when the law ticks every listener instead of those the case registered, so the
  // reloaded block's throwing listener is charged to the block placed after it.
  [Fact]
  [PlantedDefect(typeof(InfoLaw), nameof(InfoLaw.Run))]
  public void A_throwing_tick_is_named_on_its_own_block_only() {
    BlockLaws.Law law = InfoLaw.Run(
      Stand(
        Entity("tickbreaks", "test-tickbreaks"),
        Entity("unticked", "test-plain")
      ),
      "test"
    );

    Assert.Equal(2, law.Cases);
    Assert.Equal(
      [
        "test:tickbreaks ticked threw InvalidOperationException: the tick broke",
      ],
      law.Findings.Select(f => f.Split(" (")[0])
    );
  }

  // Fails when the law stops reading log entries around a step, or leaves them to the log rule,
  // which then fails this test on the Warning.
  [Fact]
  [PlantedDefect(typeof(InfoLaw), nameof(InfoLaw.Run))]
  public void Info_that_logs_is_named_fresh_ticked_and_reloaded() {
    BlockLaws.Law law = InfoLaw.Run(
      Stand(Entity("logs", "test-infologs")),
      "test"
    );

    Assert.Equal(
      [
        "test:logs info fresh logged Warning: read the info",
        "test:logs info ticked logged Warning: read the info",
        "test:logs info reloaded logged Warning: read the info",
      ],
      law.Findings
    );
  }

  // Fails when a placement that leaves no block entity passes unread.
  [Fact]
  [PlantedDefect(typeof(InfoLaw), nameof(InfoLaw.Run))]
  public void A_placement_that_leaves_no_entity_is_named() {
    BlockLaws.Law law = InfoLaw.Run(
      Stand(Entity("orphan", "test-other").Class("test-dropsentity")),
      "test"
    );

    Assert.Equal(
      ["test:orphan placed raised no test-other block entity"],
      law.Findings
    );
  }

  #endregion

  #region Reload

  // Fails when the law skips the tree or info comparison or the tick, takes the fresh tree from the
  // ticked entity, or counts two blocks without variant groups as one blocktype.
  [Fact]
  [PlantedDefect(typeof(ReloadLaw), nameof(ReloadLaw.Run))]
  public void A_count_written_and_never_read_back_is_named_by_its_tree_and_its_info() {
    BlockLaws.Law law = ReloadLaw.Run(
      Stand(Entity("forgets", "test-forgets"), Entity("keeps", "test-keeps")),
      "test"
    );

    Assert.Equal(
      [
        "test:forgets count: back at a fresh instance's value after the reload",
        "test:forgets info changed over the reload: \"count 5\" became \"count 0\"",
      ],
      law.Findings
    );
    Assert.Equal(2, law.Blocks);
  }

  // Fails when an entity of another class after the reload passes as the same one, or
  // TestWorld.Reload builds the old instance's type instead of the block's entity class.
  [Fact]
  [PlantedDefect(typeof(ReloadLaw), nameof(ReloadLaw.Run))]
  public void An_entity_that_reloads_as_another_class_is_named() {
    BlockLaws.Law law = ReloadLaw.Run(
      Stand(
        Entity("swaps", "test-swaps").Class("test-swapsblock"),
        Entity("swapped", "test-other")
      ),
      "test"
    );

    Assert.Equal(
      ["test:swaps reloaded as OtherBe, not SwapsOnPlacement"],
      law.Findings
    );
  }

  #endregion

  #region Every law

  // Fails when Stand leaves vanilla's per-thread room accessor (1.21 and later) bound to an earlier
  // world, so rooms in this one are walked over the earlier world's cells.
  [Fact]
  public void A_stood_world_walks_its_rooms_over_its_own_cells() {
    var at = new BlockPos(64, 64, 64);
    TestWorld earlier = Stand(Wall);
    earlier
      .Api.ModLoader.GetModSystem<Vintagestory.GameContent.RoomRegistry>()
      .GetRoomForPosition(at);
    TestWorld world = Stand(Wall);
    world.Accessor.SetBlock(
      world.World.GetBlock(new AssetLocation("test:wall")).BlockId,
      at
    );

    var rooms =
      world.Api.ModLoader.GetModSystem<Vintagestory.GameContent.RoomRegistry>()!;

    rooms.GetRoomForPosition(at);

#if GAME_GE_1_21
    var walked = (IBlockAccessor)
      typeof(Vintagestory.GameContent.RoomRegistry)
        .GetField(
          "blockAccessor",
          System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static
        )!
        .GetValue(null)!;
#else
    var walked = (IBlockAccessor)
      typeof(Vintagestory.GameContent.RoomRegistry)
        .GetField(
          "blockAccess",
          System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
        )!
        .GetValue(rooms)!;
#endif
    Assert.Equal("test:wall", walked.GetBlock(at).Code.ToString());
  }

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
      ["placement", "break", "multiblock", "megablock", "info", "reload"],
      result.Laws.Select(l => l.Name)
    );
#else
    Assert.Equal(
      ["placement", "multiblock", "megablock", "info", "reload"],
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

  private sealed class NorthFillers : BlockFilledMegastructure {
    protected override List<FillerCell> ReservedCells(
      IWorldAccessor world,
      BlockPos pos
    ) => StructureFillers.FootprintCells(this, pos, 0);
  }

  private sealed class ShortFillers : BlockFilledMegastructure {
    protected override List<FillerCell> ReservedCells(
      IWorldAccessor world,
      BlockPos pos
    ) => [.. FootprintCells(pos).Take(1)];
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

  private sealed class FixedFrame : SideFrame {
    protected override void UpdateStructureRotation() {
      if (Block != null)
        SetStructureAngle(0);
    }
  }

  private sealed class PlainBe : BlockEntity { }

  private sealed class PlacesAWall : Block {
    public override bool TryPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      ItemStack itemstack,
      BlockSelection blockSel,
      ref string failureCode
    ) {
      world.BlockAccessor.SetBlock(
        world.GetBlock(new AssetLocation("test:wall")).BlockId,
        blockSel.Position
      );
      return true;
    }
  }

  private sealed class PlacesKindB : Block {
    public override bool TryPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      ItemStack itemstack,
      BlockSelection blockSel,
      ref string failureCode
    ) {
      world.BlockAccessor.SetBlock(
        world.GetBlock(CodeWithVariant("kind", "b")).BlockId,
        blockSel.Position
      );
      return true;
    }
  }

  private sealed class PlacesTheBareTurner : Block {
    public override bool TryPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      ItemStack itemstack,
      BlockSelection blockSel,
      ref string failureCode
    ) {
      world.BlockAccessor.SetBlock(
        world.GetBlock(new AssetLocation("test:turner-a")).BlockId,
        blockSel.Position
      );
      return true;
    }
  }

  private sealed class PlacesNorth : Block {
    public override bool TryPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      ItemStack itemstack,
      BlockSelection blockSel,
      ref string failureCode
    ) {
      world.BlockAccessor.SetBlock(
        world.GetBlock(this.WithVariant("side", "n")).BlockId,
        blockSel.Position
      );
      return true;
    }
  }

  private sealed class ThrowsOnceTicked : BlockEntity {
    private int _ticks;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      RegisterGameTickListener(_ => _ticks++, 1000);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
      if (_ticks > 0)
        throw new InvalidOperationException($"ticked {_ticks} time(s)");
    }
  }

  private sealed class TickBreaks : BlockEntity {
    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      RegisterGameTickListener(
        _ => throw new InvalidOperationException("the tick broke"),
        1000
      );
    }
  }

  private sealed class LogsItsInfo : BlockEntity {
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) =>
      Api.Logger.Warning("read the info");
  }

  private class ForgetsItsCount : BlockEntity {
    protected int Count;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      RegisterGameTickListener(_ => Count++, 1000);
    }

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetInt("count", Count);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) =>
      dsc.Append("count ").Append(Count);
  }

  private sealed class KeepsItsCount : ForgetsItsCount {
    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    ) {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Count = tree.GetInt("count");
    }
  }

  private sealed class SwapsOnPlacement : BlockEntity { }

  private sealed class OtherBe : BlockEntity { }

  private sealed class DropsItsEntity : Block {
    public override void OnBlockPlaced(
      IWorldAccessor world,
      BlockPos blockPos,
      ItemStack byItemStack = null!
    ) {
      base.OnBlockPlaced(world, blockPos, byItemStack);
      world.BlockAccessor.RemoveBlockEntity(blockPos);
    }
  }

  private sealed class ExchangesToSwapped : Block {
    public override void OnBlockPlaced(
      IWorldAccessor world,
      BlockPos blockPos,
      ItemStack byItemStack = null!
    ) {
      base.OnBlockPlaced(world, blockPos, byItemStack);
      world.BlockAccessor.ExchangeBlock(
        world.GetBlock(new AssetLocation("test:swapped")).BlockId,
        blockPos
      );
    }
  }

  private sealed class NeverPlaces : Block {
    public override bool CanPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel,
      ref string failureCode
    ) {
      failureCode = "test-never";
      return false;
    }
  }

  #endregion
}
