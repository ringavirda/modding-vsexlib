using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The block laws against small blocks that each break one law: placements that land
/// amiss; megablocks that keep or misplace their fillers; multiblocks read in another frame or
/// naming an unregistered block; a construction that drops itself; info that throws or logs;
/// entities that forget a count or change class over a reload; blocks that change or throw when a
/// neighbour comes and goes; and a network member whose answer changes when asked again.</summary>
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
    world.RegisterClass("test-mega", typeof(BlockFilledMegastructure));
    world.RegisterClass("test-turner", typeof(TurnsOnANeighbour));
    world.RegisterClass("test-counter", typeof(CountsNeighbours));
    world.RegisterClass("test-counted", typeof(NeighbourCount));
    world.RegisterClass("test-notifythrows", typeof(ThrowsOnANeighbour));
    world.RegisterClass("test-accepter", typeof(Accepter));
    world.RegisterClass("test-refuser", typeof(RefusesTheAccepter));
    world.RegisterClass("test-asksagain", typeof(RefusesWhenFirstAsked));
    world.RegisterClass(
      "test-forgetsentity",
      typeof(DropsItsEntityOnANeighbour)
    );
    world.RegisterClass("test-askthrows", typeof(ThrowsWhenAsked));
    world.RegisterClass("test-readssupport", typeof(ReadsItsSupport));
    world.RegisterClass("test-support", typeof(Support));
    world.RegisterClass("test-askedonce", typeof(AcceptsWhenFirstAsked));
    world.RegisterClass("test-initthrows", typeof(ThrowsOnInitialize));
    world.RegisterClass("test-namesanitem", typeof(NamesAnItem));
    world.RegisterClass("test-namestwo", typeof(NamesTwoTogether));
    world.RegisterClass("test-clickthrows", typeof(ThrowsOnAClick));
    world.RegisterClass("test-readsstack", typeof(ReadsItsStack));
    world.RegisterClass("test-keepscell", typeof(ReadsItsStackInPlace));
    world.RegisterClass("test-lights", typeof(Lights));
    world.RegisterClass("test-spent", typeof(SpentOnTheStart));
    world.RegisterClass("test-holder", typeof(Holder));
    world.RegisterClass("test-holderblock", typeof(DropsWhatItHolds));
    world.RegisterClass("test-forgetsheld", typeof(ForgetsWhatItHolds));
    world.RegisterClass("test-shelf", typeof(Shelf));
    world.RegisterClass("test-shedsnothing", typeof(ShedsNothing));
    world.RegisterClass("test-pair", typeof(TakesAMatchingPair));
    world.RegisterClass("test-loadmoves", typeof(MovesOnTheLoad));
    world.RegisterClass("test-feeder", typeof(FeedsWhenFormed));
  }

  private static readonly ExItemDef Token = ExItemDef.Create("test", "token");

  private static readonly ExItemDef Nail = ExItemDef.Create("test", "nail");

  private static ExBlockDef Holds(string code, string entity, string cls) =>
    Names(code, "test:token", takes: true).Class(cls).EntityClass(entity);

  private static TestWorld Hands(ExItemDef[] items, params ExBlockDef[] defs) =>
    BlockLaws.Stand(defs, Exlib, Prepare, items);

  private static ExBlockDef Names(
    string code,
    string item,
    bool takes,
    bool spends = false
  ) =>
    ExBlockDef
      .Create("test", code)
      .Class("test-namesanitem")
      .Attribute("names", item)
      .Attribute("takes", takes)
      .Attribute("spends", spends);

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

  private static ExBlockDef Feeds(
    string code,
    string entity,
    bool dead = false
  ) =>
    ExBlockDef
      .Create("test", code)
      .Class("test-feeder")
      .EntityClass(entity)
      .Attribute("dead", dead);

  private static ExBlockDef Entity(string code, string entity) =>
    ExBlockDef.Create("test", code).EntityClass(entity);

  private static FillerBehaviorSpec Member(string face) =>
    FillerBehaviorSpec.Of<BEBehaviorNetworkMember>(
      face,
      new { networkType = "test" }
    );

  private static ExBlockDef Node(string code, string cls) =>
    ExBlockDef
      .Create("test", code)
      .Class(cls)
      .VariantGroup("orientation", "ns", "nsew");

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
          .Behavior("BlockEntityInteract")
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

  #region Neighbour

  // Fails when the law skips the code, the entity or the tree comparison, stops recording a
  // throwing notification, or tries the face the block stands on.
  [Fact]
  [PlantedDefect(typeof(NeighbourLaw), nameof(NeighbourLaw.Run))]
  public void A_block_that_turns_counts_forgets_or_throws_on_a_neighbour_is_named() {
    BlockLaws.Law law = NeighbourLaw.Run(
      Stand(
        Wall,
        ExBlockDef.Create("test", "turner").Class("test-turner"),
        Entity("counter", "test-counted").Class("test-counter"),
        Entity("forgets", "test-plain").Class("test-forgetsentity"),
        ExBlockDef.Create("test", "throws").Class("test-notifythrows")
      ),
      "test"
    );

    Assert.Equal(
      [
        "test:counter with a neighbour on its north face changed the tree of its cell at "
          + "count",
        "test:forgets with a neighbour on its north face left its cell without its block "
          + "entity",
        "test:throws with a neighbour on its north face threw InvalidOperationException: "
          + "a neighbour changed",
        "test:turner with a neighbour on its north face left its cell holding test:wall, not "
          + "test:turner",
      ],
      law.Findings.Select(f => f.Split(" (")[0])
    );
    Assert.Equal(5, law.Blocks);
    Assert.Equal(4 + 5, law.Cases);
  }

  // Fails when the law stands the block's support only after placing it.
  [Fact]
  public void A_block_that_reads_its_support_is_judged_from_where_it_stands() {
    BlockLaws.Law law = NeighbourLaw.Run(
      Stand(Entity("reads", "test-support").Class("test-readssupport")),
      "test"
    );

    Assert.Empty(law.Findings);
    Assert.Equal(5, law.Cases);
  }

  // Fails when the law skips the fillers' cells, or sets a neighbour over a cell the structure
  // holds.
  [Fact]
  public void A_megablock_meets_neighbours_on_the_free_faces_of_its_fillers_too() {
    BlockLaws.Law law = NeighbourLaw.Run(
      Stand(Mega("mega", "test-mega")),
      "test"
    );

    Assert.Empty(law.Findings);
    Assert.Equal(4 * (3 + 4 + 4), law.Cases);
  }

  #endregion

  #region Network

  // Fails when the law walks from one member of a pair only.
  [Fact]
  [PlantedDefect(typeof(NetworkLaw), nameof(NetworkLaw.Run))]
  public void A_member_that_answers_a_second_asking_otherwise_is_named() {
    BlockLaws.Law law = NetworkLaw.Run(
      Stand(
        Node("accepter", "test-accepter"),
        Node("asks", "test-asksagain"),
        Node("once", "test-askedonce")
      ),
      "test"
    );

    Assert.Equal(
      [
        "test:accepter-ns test at (0, 0, 0) across its north face and test:asks-ns at (0, 0, 0): "
          + TheOtherReaches,
        "test:accepter-ns test at (0, 0, 0) across its north face and test:once-ns at (0, 0, 0): "
          + ItReaches,
        "test:accepter-nsew test at (0, 0, 0) across its east face and test:asks-nsew at "
          + "(0, 0, 0): "
          + TheOtherReaches,
        "test:accepter-nsew test at (0, 0, 0) across its east face and test:once-nsew at "
          + "(0, 0, 0): "
          + ItReaches,
        "test:asks-ns test at (0, 0, 0) across its north face and test:asks-ns at (0, 0, 0): "
          + TheOtherReaches,
        "test:asks-ns test at (0, 0, 0) across its north face and test:once-ns at (0, 0, 0): "
          + TheOtherReaches,
        "test:asks-nsew test at (0, 0, 0) across its east face and test:asks-nsew at (0, 0, 0): "
          + TheOtherReaches,
        "test:asks-nsew test at (0, 0, 0) across its east face and test:once-nsew at (0, 0, 0): "
          + TheOtherReaches,
      ],
      law.Findings
    );
  }

  private const string ItReaches =
    "its walk reaches the other, the other's does not reach it";

  private const string TheOtherReaches =
    "the other's walk reaches it, its own does not reach the other";

  // Fails when the law stops catching a placement or a walk that throws.
  [Fact]
  public void A_member_whose_placement_or_walk_throws_is_named() {
    BlockLaws.Law law = NetworkLaw.Run(
      Stand(
        Node("accepter", "test-accepter"),
        Node("askthrows", "test-askthrows"),
        Node("initthrows", "test-accepter").EntityClass("test-initthrows")
      ),
      "test"
    );

    string[] heads = [.. law.Findings.Select(f => f[..f.IndexOf(" (at ")])];
    Assert.Equal(
      [
        "test:initthrows-ns placed threw InvalidOperationException: initialised",
        "test:initthrows-nsew placed threw InvalidOperationException: initialised",
      ],
      heads.Where(f => f.Contains(" placed threw "))
    );
    Assert.Equal(
      12,
      heads.Count(f =>
        f.Contains("test:askthrows-")
        && f.EndsWith(": the walk threw InvalidOperationException: asked")
      )
    );
    Assert.Equal(14, heads.Length);
  }

  // Fails when the law stands a pair whose second structure overlaps the first. Each port of
  // "ported" faces its own principal, so every pair it makes overlaps; the accepter's pairs stand.
  [Fact]
  public void A_pair_whose_structures_would_overlap_is_skipped() {
    BlockLaws.Law law = NetworkLaw.Run(
      Stand(
          ExBlockDef
            .Create("test", "ported")
            .Class("test-mega")
            .SideVariant()
            .FillerOffsets([
              new(-1, 0, 0, Behaviors: [Member("east")]),
              new(1, 0, 0, Behaviors: [Member("west")]),
            ]),
          Node("accepter", "test-accepter")
        )
        .RegisterNetwork("test", system => new TestNetwork(system)),
      "test"
    );

    Assert.Empty(law.Findings);
    Assert.Equal(2, law.Blocks);
    Assert.Equal(4, law.Cases);
  }

  // Fails when IsValidNetworkNeighbour asks only the walk's source whether it accepts the other.
  [Fact]
  public void A_refusal_from_one_side_keeps_the_pair_apart_from_both() {
    BlockLaws.Law law = NetworkLaw.Run(
      Stand(Node("accepter", "test-accepter"), Node("refuser", "test-refuser")),
      "test"
    );

    Assert.Empty(law.Findings);
    Assert.Equal(2, law.Blocks);
    Assert.Equal(4 * 2 * 2, law.Cases);
  }

  #endregion

  #region Interaction

  // Fails when a taken click with a named item is not judged for what it changed.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_click_its_help_names_that_changes_nothing_is_named() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token],
        Names("idle", "test:token", takes: true),
        Names("spends", "test:token", takes: true, spends: true)
      ),
      "test"
    );

    Assert.Equal(2, law.Blocks);
    Assert.Equal(
      [
        "test:idle clicked on its cell with test:token, which its help names for "
          + "\"test:use\", and the click changed nothing",
      ],
      law.Findings
    );
  }

  // Fails when a click the block declines is not handed to the held item, as the engine hands it,
  // or when a click both decline passes.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_click_the_block_and_the_item_both_refuse_is_named() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token, ExItemDef.Create("test", "lights").Class("test-lights")],
        Names("refuses", "test:token", takes: false),
        Names("lit", "test:lights", takes: false)
      ),
      "test"
    );

    Assert.Equal(
      [
        "test:refuses clicked on its cell with test:token, which its help names for "
          + "\"test:use\", and the click was refused",
      ],
      law.Findings
    );
  }

  // Fails when the held item's step and stop run on the collectible the hand held before the
  // start rather than the one it holds after.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_held_item_the_start_swaps_is_stepped_as_what_the_hand_now_holds() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token, ExItemDef.Create("test", "spent").Class("test-spent")],
        Names("pours", "test:spent", takes: false)
      ),
      "test"
    );

    Assert.Equal(2, law.Cases);
    Assert.Empty(law.Findings);
  }

  // Fails when the keys a help line names are not held for its click.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_click_is_made_with_the_keys_its_help_names() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token],
        Names("sneaks", "test:token", takes: true, spends: true)
          .Attribute("keys", "sneak")
      ),
      "test"
    );

    Assert.Equal(2, law.Cases);
    Assert.Empty(law.Findings);
  }

  // Fails when the other items a help lists under the same action are not carried beside the
  // hand.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void Items_listed_together_are_carried_together() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token, Nail],
        ExBlockDef.Create("test", "together").Class("test-namestwo")
      ),
      "test"
    );

    Assert.Equal(3, law.Cases);
    Assert.Empty(law.Findings);
  }

  // Fails when the empty-hand click is dropped, or a throwing click passes.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_click_that_throws_is_named() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands([], ExBlockDef.Create("test", "throws").Class("test-clickthrows")),
      "test"
    );

    Assert.Equal(
      [
        "test:throws clicked on its cell with an empty hand threw "
          + "InvalidOperationException: clicked",
      ],
      law.Findings.Select(f => f.Split(" (")[0])
    );
  }

  // Fails when the stack placed from carries its position, the entity's cell is not read after
  // the placement or the reload, or the spawn skips the entity's own OnBlockPlaced.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void An_entity_placed_from_a_stack_or_reloaded_off_its_cell_is_named() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [],
        Entity("moves", "test-readsstack"),
        Entity("stays", "test-keepscell"),
        Entity("loadmoves", "test-loadmoves")
      ),
      "test"
    );

    Assert.Equal(2, law.Findings.Count);
    Assert.StartsWith(
      "test:loadmoves reloaded left its block entity at ",
      law.Findings[0]
    );
    Assert.Equal(
      "test:moves placed left its block entity at 0, 0, 0, not at its cell",
      law.Findings[1]
    );
  }

  // Fails when the carried item is not the one whose variant agrees with the hand's, as a stage
  // storing a metal takes one metal throughout.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void The_item_carried_beside_the_hand_agrees_with_its_variant() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [
          ExItemDef.Create("test", "plate").VariantGroup("metal", "a", "b"),
          ExItemDef.Create("test", "rivet").VariantGroup("metal", "a", "b"),
        ],
        ExBlockDef.Create("test", "pair").Class("test-pair")
      ),
      "test"
    );

    Assert.Equal(5, law.Cases);
    Assert.Empty(law.Findings);
  }

  // Fails when the hand laws' worlds start no block reinforcement system, which vanilla's lockable
  // blocks ask on every click.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_lockable_block_is_clicked_in_a_world_with_reinforcement() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands([], ExBlockDef.Create("test", "locked").Behavior("Lockable")),
      "test"
    );

    Assert.Equal(1, law.Cases);
    Assert.Empty(law.Findings);
  }

  // Fails when the hand laws' worlds leave out the vanilla tools, vessels and role materials the
  // family's help names.
  [Fact]
  public void The_hand_laws_world_holds_vanilla_tools_vessels_and_role_materials() {
    TestWorld world = Hands([]);

    Assert.NotNull(
      world.World.GetItem(new AssetLocation("game:wrench-copper"))
    );
    Assert.NotNull(
      world.World.GetItem(new AssetLocation("game:chisel-copper"))
    );
    Assert.NotNull(world.World.GetItem(new AssetLocation("game:clay-fire")));
    Assert.NotNull(
      world.World.GetBlock(new AssetLocation("game:torch-basic-lit-up"))
    );
#if GAME_GE_1_21
    Assert.NotNull(
      world.World.GetBlock(new AssetLocation("game:crucible-blue-smelted"))
    );
#else
    Assert.NotNull(
      world.World.GetBlock(new AssetLocation("game:crucible-smelted"))
    );
#endif
    Assert.NotNull(world.World.GetItem(new AssetLocation("game:crushed-iron")));
    Assert.NotNull(world.World.GetItem(new AssetLocation("game:lime")));
    Assert.NotNull(world.World.GetItem(new AssetLocation("game:coke")));
    Assert.NotNull(
      world.World.GetItem(new AssetLocation("game:metalbit-steel"))
    );
  }

#if GAME_GE_1_22
  // Fails when a construction's ingredients get no stand-ins, or a wildcard's stand-ins carry no
  // variant under the key the stage stores, which the next stage's ingredient then names.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_construction_is_paid_with_stand_ins_for_its_ingredients()
  {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [],
        ExBlockDef
          .Create("test", "frame")
          .Class("ExFilledMegastructure")
          .FillerOffsets(TwoCells)
          .EntityClass("test-plain")
          .Behavior("BlockEntityInteract")
          .Construction(c =>
            c.Stage(s => s.Require("test:peg", 1))
              .Stage(s => s.RequireMetalPlate("test", 2))
              .Stage(s => s.RequireMetalPlate("test", 1))
          )
      ),
      "test"
    );

    Assert.Equal(1, law.Blocks);
    Assert.Empty(law.Findings);
    Assert.Equal(9, law.Cases);
  }
#endif

  // Fails when the law places a block the game never sets, as it does a vessel stored on the
  // ground.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_block_the_game_never_places_is_not_clicked() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [],
        ExBlockDef
          .Create("test", "vessel")
          .Class("test-clickthrows")
          .Behavior("Unplaceable")
      ),
      "test"
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(0, law.Cases);
  }

  // Fails when the audit line the engine writes for every stack a player takes is read as a
  // fault.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void An_audited_click_is_no_fault() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token],
        Names("audits", "test:token", takes: true, spends: true)
          .Attribute("audits", true)
      ),
      "test"
    );

    Assert.Empty(law.Findings);
  }

  // Fails when a help line carrying items and a ShouldApply, which the engine ignores there, is not
  // named.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.Run))]
  public void A_line_with_items_and_a_should_apply_is_named() {
    BlockLaws.Law law = InteractionLaw.Run(
      Hands(
        [Token],
        Names("gated", "test:token", takes: true, spends: true)
          .Attribute("gated", true)
      ),
      "test"
    );

    Assert.Equal(
      [
        "test:gated help on its cell for \"test:use\" carries items and a ShouldApply, "
          + "which the engine ignores on a line with items",
      ],
      law.Findings
    );
  }

  // Fails when the formed pass clicks only the anchor or the stand-ins too, forms every facing, skips
  // the filled cells' entities, stops a structure at its first finding, or names no block clicked.
  [Fact]
  [PlantedDefect(typeof(InteractionLaw), nameof(InteractionLaw.RunFormed))]
  public void A_cell_that_answers_only_in_a_formed_structure_is_clicked_there() {
    ExBlockDef[] defs =
    [
      Feeds("feeder", "test-holder"),
      Feeds("deadfeeder", "test-holder", dead: true),
      Structure("fed", "test-sideframe", "test:feeder"),
      Structure("starved", "test-sideframe", "test:deadfeeder"),
    ];

    BlockLaws.Law alone = InteractionLaw.Run(Hands([Token], defs), "test");
    BlockLaws.Law law = InteractionLaw.RunFormed(Hands([Token], defs), "test");

    Assert.Empty(alone.Findings);
    Assert.Equal(2, law.Blocks);
    Assert.Equal(10, law.Cases);
    Assert.Equal(
      [
        "test:starved-n formed clicked on its cell at (1, 0, 0), test:deadfeeder, with "
          + "test:token, which its help names for \"test:feed\", and the click changed nothing",
        "test:starved-n formed clicked on its cell at (-1, 1, 2), test:deadfeeder, with "
          + "test:token, which its help names for \"test:feed\", and the click changed nothing",
      ],
      law.Findings
    );
  }

  #endregion

  #region Container

  // Fails when the reload count or the break drops are not compared with what the entity held,
  // or a click that hands a stack over is not read as accepting it.
  [Fact]
  [PlantedDefect(typeof(ContainerLaw), nameof(ContainerLaw.Run))]
  public void A_stack_accepted_and_lost_on_a_reload_or_a_break_is_named() {
    BlockLaws.Law law = ContainerLaw.Run(
      Hands(
        [Token],
        Holds("holds", "test-holder", "test-holderblock"),
        Holds("forgets", "test-forgetsheld", "test-holderblock"),
        Holds("keepsnothing", "test-holder", "test-namesanitem")
      ),
      "test"
    );

    Assert.Equal(3, law.Blocks);
    Assert.Equal(3, law.Cases);
    Assert.Equal(
      [
        "test:forgets held 1 test:token clicked on its cell with test:token, and 0 after "
          + "the reload",
        "test:keepsnothing held 1 test:token clicked on its cell with test:token, and "
          + "broken dropped 0",
      ],
      law.Findings
    );
  }

  // Fails when an entity with an inventory is offered nothing when its clicks accept nothing, or
  // the stacks its inventory holds inside its tree are not counted.
  [Fact]
  [PlantedDefect(typeof(ContainerLaw), nameof(ContainerLaw.Run))]
  public void An_inventory_is_offered_stacks_its_clicks_do_not_name() {
    BlockLaws.Law law = ContainerLaw.Run(
      Hands(
        [Token],
        Entity("shelf", "test-shelf"),
        Entity("sheds", "test-shedsnothing")
      ),
      "test"
    );

    Assert.Equal(2, law.Cases);
    Assert.Equal(
      [
        "test:sheds held 1 game:waterportion put into its inventory, and broken "
          + "dropped 0",
      ],
      law.Findings
    );
  }

  // Fails when the formed pass follows a stack at the anchor rather than the cell that took it,
  // skips the cells' entities, or stops reading the reload count.
  [Fact]
  [PlantedDefect(typeof(ContainerLaw), nameof(ContainerLaw.RunFormed))]
  public void A_stack_a_formed_cell_accepts_and_loses_on_a_reload_is_named() {
    BlockLaws.Law law = ContainerLaw.RunFormed(
      Hands(
        [Token],
        Feeds("feeder", "test-holder"),
        Feeds("forgetful", "test-forgetsheld"),
        Structure("kept", "test-sideframe", "test:feeder"),
        Structure("lost", "test-sideframe", "test:forgetful")
      ),
      "test"
    );

    Assert.Equal(2, law.Blocks);
    Assert.Equal(4, law.Cases);
    Assert.Equal(
      [
        "test:lost-n formed, test:forgetful on its cell at (1, 0, 0), held 1 test:token "
          + "clicked on its cell with test:token, and 0 after the reload",
        "test:lost-n formed, test:forgetful on its cell at (-1, 1, 2), held 1 test:token "
          + "clicked on its cell with test:token, and 0 after the reload",
      ],
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
      [
        "placement",
        "break",
        "multiblock",
        "megablock",
        "info",
        "reload",
        "neighbour",
        "network",
        "interaction",
        "container",
        "formed interaction",
        "formed container",
      ],
      result.Laws.Select(l => l.Name)
    );
#else
    Assert.Equal(
      [
        "placement",
        "multiblock",
        "megablock",
        "info",
        "reload",
        "neighbour",
        "network",
        "interaction",
        "container",
        "formed interaction",
        "formed container",
      ],
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

  private sealed class TurnsOnANeighbour : Block {
    public override void OnNeighbourBlockChange(
      IWorldAccessor world,
      BlockPos pos,
      BlockPos neibpos
    ) =>
      world.BlockAccessor.ExchangeBlock(
        world.GetBlock(new AssetLocation("test:wall")).BlockId,
        pos
      );
  }

  private sealed class CountsNeighbours : Block {
    public override void OnNeighbourBlockChange(
      IWorldAccessor world,
      BlockPos pos,
      BlockPos neibpos
    ) {
      if (world.BlockAccessor.GetBlockEntity(pos) is NeighbourCount counted)
        counted.Count++;
    }
  }

  private sealed class NeighbourCount : BlockEntity {
    public int Count;

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetInt("count", Count);
    }
  }

  private sealed class ThrowsOnANeighbour : Block {
    public override void OnNeighbourBlockChange(
      IWorldAccessor world,
      BlockPos pos,
      BlockPos neibpos
    ) => throw new InvalidOperationException("a neighbour changed");
  }

  private sealed class DropsItsEntityOnANeighbour : Block {
    public override void OnNeighbourBlockChange(
      IWorldAccessor world,
      BlockPos pos,
      BlockPos neibpos
    ) => world.BlockAccessor.RemoveBlockEntity(pos);
  }

  private sealed class ReadsItsSupport : Block {
    public override void OnBlockPlaced(
      IWorldAccessor world,
      BlockPos blockPos,
      ItemStack byItemStack = null!
    ) {
      base.OnBlockPlaced(world, blockPos, byItemStack);
      Read(world, blockPos);
    }

    public override void OnNeighbourBlockChange(
      IWorldAccessor world,
      BlockPos pos,
      BlockPos neibpos
    ) => Read(world, pos);

    private static void Read(IWorldAccessor world, BlockPos pos) {
      if (world.BlockAccessor.GetBlockEntity(pos) is Support support)
        support.Below = world
          .BlockAccessor.GetBlock(pos.DownCopy())
          .Code.ToString();
    }
  }

  private sealed class Support : BlockEntity {
    public string Below = "";

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetString("below", Below);
    }
  }

  /// <summary>Accepts a block the first time it is asked about it and refuses it after.</summary>
  private sealed class AcceptsWhenFirstAsked : Accepter {
    private readonly HashSet<int> _asked = [];

    public override bool AcceptsNeighbour(Block neighbour) =>
      _asked.Add(neighbour.BlockId);
  }

  private sealed class ThrowsWhenAsked : Accepter {
    public override bool AcceptsNeighbour(Block neighbour) =>
      throw new InvalidOperationException("asked");
  }

  private sealed class ThrowsOnInitialize : BlockEntity {
    public override void Initialize(ICoreAPI api) =>
      throw new InvalidOperationException("initialised");
  }

  private sealed class TestNetwork(BlockNetworkModSystem system)
    : BlockNetwork(system) {
    public override string NetworkType => "test";

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) { }

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) { }
  }

  private class Accepter : BlockNetworkNode {
    public override string NetworkType => "test";
  }

  private sealed class RefusesTheAccepter : Accepter {
    public override bool AcceptsNeighbour(Block neighbour) =>
      neighbour is not Accepter || neighbour is RefusesTheAccepter;
  }

  /// <summary>Refuses a block the first time it is asked about it and accepts it after.</summary>
  private sealed class RefusesWhenFirstAsked : Accepter {
    private readonly HashSet<int> _asked = [];

    public override bool AcceptsNeighbour(Block neighbour) =>
      !_asked.Add(neighbour.BlockId);
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

  private class NamesAnItem : Block {
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
      IWorldAccessor world,
      BlockSelection selection,
      IPlayer forPlayer
    ) =>
      [
        new()
        {
          ActionLangCode = "test:use",
          MouseButton = EnumMouseButton.Right,
          HotKeyCode = Attributes["keys"].AsString(),
          ShouldApply = Attributes["gated"].AsBool()
            ? (wi, bs, es) => true
            : null,
          Itemstacks =
          [
            new ItemStack(
              world.GetItem(new AssetLocation(Attributes["names"].AsString()))
            ),
          ],
        },
      ];

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
      if (!Attributes["takes"].AsBool() || hand.Empty)
        return Attributes["takes"].AsBool();
      bool keyed =
        Attributes["keys"].AsString() == null
        || byPlayer.Entity.Controls.ShiftKey;
      if (Attributes["spends"].AsBool() && keyed) {
        hand.TakeOut(1);
        if (Attributes["audits"].AsBool())
          world.Logger.Audit("{0} spent a token", byPlayer.PlayerName);
      }
      if (
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is Holder holder
      )
        holder.Take(hand);
      return true;
    }
  }

  private sealed class NamesTwoTogether : Block {
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
      IWorldAccessor world,
      BlockSelection selection,
      IPlayer forPlayer
    ) =>
      [
        .. new[] { "test:token", "test:nail" }.Select(
          code => new WorldInteraction
          {
            ActionLangCode = "test:build",
            MouseButton = EnumMouseButton.Right,
            Itemstacks =
            [
              new ItemStack(world.GetItem(new AssetLocation(code))),
            ],
          }
        ),
      ];

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      ItemSlot[] held =
      [
        .. byPlayer
          .InventoryManager.GetHotbarInventory()
          .Where(slot => !slot.Empty),
      ];
      if (held.Length < 2)
        return true;
      foreach (ItemSlot slot in held)
        slot.TakeOut(1);
      return true;
    }
  }

  private sealed class TakesAMatchingPair : Block {
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
      IWorldAccessor world,
      BlockSelection selection,
      IPlayer forPlayer
    ) =>
      [
        .. new[] { "plate", "rivet" }.Select(kind => new WorldInteraction
        {
          ActionLangCode = "test:build",
          MouseButton = EnumMouseButton.Right,
          Itemstacks =
          [
            .. new[] { "a", "b" }.Select(metal => new ItemStack(
              world.GetItem(new AssetLocation($"test:{kind}-{metal}"))
            )),
          ],
        }),
      ];

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      ItemSlot[] held =
      [
        .. byPlayer
          .InventoryManager.GetHotbarInventory()
          .Where(slot => !slot.Empty),
      ];
      if (
        held.Length == 2
        && held[0].Itemstack.Collectible.Variant["metal"]
          == held[1].Itemstack.Collectible.Variant["metal"]
      )
        foreach (ItemSlot slot in held)
          slot.TakeOut(1);
      return true;
    }
  }

  private sealed class ThrowsOnAClick : Block {
    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) => throw new InvalidOperationException("clicked");
  }

  private class ReadsItsStack : BlockEntity {
    public override void OnBlockPlaced(ItemStack? byItemStack = null) {
      if (
        byItemStack?.Attributes["blockEntityAttributes"] is ITreeAttribute tree
      )
        FromTreeAttributes(tree, Api.World);
    }
  }

  private sealed class ReadsItsStackInPlace : ReadsItsStack {
    public override void OnBlockPlaced(ItemStack? byItemStack = null) {
      BlockPos at = Pos.Copy();
      base.OnBlockPlaced(byItemStack);
      Pos = at;
    }
  }

  private sealed class MovesOnTheLoad : BlockEntity {
    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    ) {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Pos = Pos.UpCopy();
    }
  }

  private sealed class Lights : Item {
    public override void OnHeldInteractStart(
      ItemSlot slot,
      EntityAgent byEntity,
      BlockSelection blockSel,
      EntitySelection entitySel,
      bool firstEvent,
      ref EnumHandHandling handling
    ) {
      slot.TakeOut(1);
      handling = EnumHandHandling.PreventDefault;
    }
  }

  private sealed class SpentOnTheStart : Item {
    public override void OnHeldInteractStart(
      ItemSlot slot,
      EntityAgent byEntity,
      BlockSelection blockSel,
      EntitySelection entitySel,
      bool firstEvent,
      ref EnumHandHandling handling
    ) {
      slot.Itemstack = new ItemStack(
        byEntity.World.GetItem(new AssetLocation("test:token"))
      );
      handling = EnumHandHandling.PreventDefault;
    }

    public override bool OnHeldInteractStep(
      float secondsUsed,
      ItemSlot slot,
      EntityAgent byEntity,
      BlockSelection blockSel,
      EntitySelection entitySel
    ) => throw new InvalidOperationException("stepped once spent");
  }

  private class Holder : BlockEntity {
    protected ItemStack? Held;

    internal void Take(ItemSlot hand) {
      if (hand.Empty && Held == null)
        return;
      ItemStack taken = hand.TakeOut(1);
      if (Held == null)
        Held = taken;
      else
        Held.StackSize += taken.StackSize;
      MarkDirty();
    }

    internal ItemStack? Contents => Held;

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      if (Held != null)
        tree.SetItemstack("held", Held);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldForResolving
    ) {
      base.FromTreeAttributes(tree, worldForResolving);
      Held = tree.GetItemstack("held");
      Held?.ResolveBlockOrItem(worldForResolving);
    }
  }

  private sealed class ForgetsWhatItHolds : Holder {
    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldForResolving
    ) {
      base.FromTreeAttributes(tree, worldForResolving);
      Held = null;
    }
  }

  private sealed class DropsWhatItHolds : NamesAnItem {
    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) =>
      [
        .. base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [],
        .. world.BlockAccessor.GetBlockEntity(pos)
          is Holder { Contents: { } held }
          ? new[] { held.Clone() }
          : [],
      ];
  }

  private sealed class FeedsWhenFormed : Block {
    private static bool Formed(IWorldAccessor world, BlockPos pos) =>
      BlockEntityMultiblockStructure.FindAnchorOwning<BlockEntityMultiblockStructure>(
        world,
        pos,
        3,
        3,
        3
      )
        is { StructureComplete: true };

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
      IWorldAccessor world,
      BlockSelection selection,
      IPlayer forPlayer
    ) =>
      Formed(world, selection.Position)
        ?
        [
          new()
          {
            ActionLangCode = "test:feed",
            MouseButton = EnumMouseButton.Right,
            Itemstacks =
            [
              new ItemStack(world.GetItem(new AssetLocation("test:token"))),
            ],
          },
        ]
        : [];

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      if (!Formed(world, blockSel.Position))
        return false;
      if (
        Attributes?["dead"].AsBool() != true
        && world.BlockAccessor.GetBlockEntity(blockSel.Position)
          is Holder holder
      )
        holder.Take(byPlayer.InventoryManager.ActiveHotbarSlot);
      return true;
    }

    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) =>
      [
        .. base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [],
        .. world.BlockAccessor.GetBlockEntity(pos)
          is Holder { Contents: { } held }
          ? new[] { held.Clone() }
          : [],
      ];
  }

  private class Shelf : BlockEntityContainer {
    private readonly InventoryGeneric _inventory = new(1, "shelf-0", null);

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "shelf";
  }

  private sealed class ShedsNothing : Shelf {
    public override void OnBlockBroken(IPlayer? byPlayer = null) { }
  }

  #endregion
}
