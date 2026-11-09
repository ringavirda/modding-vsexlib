using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Casting;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>Pins the sand station's interaction precedence, fill order, yield, misrun and intake gates, and filling shape.</summary>
public class SandStationLogicTests {
  private static SandStationAction EmptyHand(
    SandStationLevel sand,
    bool impression,
    bool metal,
    bool hardened
  ) =>
    SandStationLogic.Decide(
      false,
      false,
      true,
      sand,
      impression,
      metal,
      hardened
    );

  [Fact]
  public void Empty_hand_on_a_hardened_cast_harvests() {
    Assert.Equal(
      SandStationAction.Harvest,
      EmptyHand(SandStationLevel.Full, true, true, true)
    );
  }

  [Fact]
  public void Empty_hand_on_a_still_liquid_cast_is_refused_not_fall_through() {
    Assert.Equal(
      SandStationAction.TooHot,
      EmptyHand(SandStationLevel.Full, true, true, false)
    );
  }

  [Fact]
  public void Metal_present_blocks_ramming_and_imprinting() {
    // Holding sand or a pattern does nothing while metal is in the station.
    Assert.Equal(
      SandStationAction.None,
      SandStationLogic.Decide(
        true,
        false,
        false,
        SandStationLevel.Full,
        true,
        true,
        false
      )
    );
    Assert.Equal(
      SandStationAction.None,
      SandStationLogic.Decide(
        false,
        true,
        false,
        SandStationLevel.Full,
        true,
        true,
        true
      )
    );
  }

  [Theory]
  [InlineData(SandStationLevel.Empty)]
  [InlineData(SandStationLevel.Half)]
  public void Sand_rams_an_empty_or_half_station_to_full(SandStationLevel sand) {
    Assert.Equal(
      SandStationAction.RamSand,
      SandStationLogic.Decide(true, false, false, sand, false, false, false)
    );
  }

  [Fact]
  public void Sand_on_a_full_station_does_nothing() {
    Assert.Equal(
      SandStationAction.None,
      SandStationLogic.Decide(
        true,
        false,
        false,
        SandStationLevel.Full,
        false,
        false,
        false
      )
    );
  }

  [Fact]
  public void A_pattern_imprints_only_full_impression_free_sand() {
    Assert.Equal(
      SandStationAction.Imprint,
      SandStationLogic.Decide(
        false,
        true,
        false,
        SandStationLevel.Full,
        false,
        false,
        false
      )
    );
    // Not on half sand,
    Assert.Equal(
      SandStationAction.None,
      SandStationLogic.Decide(
        false,
        true,
        false,
        SandStationLevel.Half,
        false,
        false,
        false
      )
    );
    // and over an existing impression it presses another piece.
    Assert.Equal(
      SandStationAction.Press,
      SandStationLogic.Decide(
        false,
        true,
        false,
        SandStationLevel.Full,
        true,
        false,
        false
      )
    );
  }

  [Fact]
  // Fails when a chain fills its pieces in reverse pressing order.
  public void A_chain_fills_the_first_pressed_piece_before_the_second() {
    IReadOnlyList<int[]> batches = SandStationLogic.FillBatches(
      FillOrder.Chain,
      3,
      3
    );

    int[] fills = SandStationLogic.PieceFills(150, 100, batches);

    Assert.Equal([100, 50, 0], fills);
  }

  [Fact]
  // Fails when the two-row stock fills one piece at a time instead of in pairs.
  public void Two_rows_fill_a_front_pair_then_the_pair_behind_it_outward() {
    Assert.Equal(
      new[] { new[] { 0, 1 }, new[] { 2, 3 } },
      SandStationLogic.FillBatches(FillOrder.Rows, 12, 4).ToArray()
    );
    Assert.Equal(
      new[]
      {
        new[] { 0, 1 },
        new[] { 6, 7 },
        new[] { 2, 3 },
        new[] { 8, 9 },
        new[] { 4, 5 },
        new[] { 10, 11 },
      },
      SandStationLogic.FillBatches(FillOrder.Rows, 12, 12).ToArray()
    );
    Assert.Equal(
      new[] { new[] { 0, 1 }, new[] { 6 }, new[] { 2, 3 }, new[] { 4, 5 } },
      SandStationLogic.FillBatches(FillOrder.Rows, 12, 7).ToArray()
    );
  }

  [Fact]
  // Fails when a short batch hands its metal to one piece instead of sharing it evenly.
  public void A_short_batch_shares_the_remaining_metal_evenly() {
    IReadOnlyList<int[]> batches = SandStationLogic.FillBatches(
      FillOrder.Rows,
      12,
      4
    );

    int[] fills = SandStationLogic.PieceFills(300, 100, batches);

    Assert.Equal([100, 100, 50, 50], fills);
  }

  [Fact]
  // Fails when slabs fill one piece at a time, or the parsed "pairs" token is read as a chain.
  public void A_pairs_filling_takes_two_pieces_a_batch_and_shares_them_evenly() {
    IReadOnlyList<int[]> batches = SandStationLogic.FillBatches(
      FillOrder.Pairs,
      2,
      2
    );

    Assert.Equal(new[] { new[] { 0, 1 } }, batches.ToArray());
    Assert.Equal(
      [2700, 2700],
      SandStationLogic.PieceFills(5400, 5400, batches)
    );
  }

  [Fact]
  // Fails when a pair left 2u-1 counts the piece that took the odd unit as a part.
  public void A_pair_short_by_one_unit_yields_no_part() {
    IReadOnlyList<int[]> batches = SandStationLogic.FillBatches(
      FillOrder.Pairs,
      2,
      2
    );

    SandStationYield yield = SandStationLogic.YieldOf(
      false,
      1199,
      600,
      batches
    );

    Assert.Equal(new SandStationYield(0, 1199), yield);
  }

  [Fact]
  // Fails when a batch filled to exactly its units is not counted as parts, or the misrun branch keeps parts.
  public void A_full_batch_yields_its_parts_and_a_misrun_yields_only_scrap() {
    IReadOnlyList<int[]> batches = SandStationLogic.FillBatches(
      FillOrder.Pairs,
      2,
      2
    );

    Assert.Equal(
      new SandStationYield(2, 0),
      SandStationLogic.YieldOf(false, 1200, 600, batches)
    );
    Assert.Equal(
      new SandStationYield(0, 1200),
      SandStationLogic.YieldOf(true, 1200, 600, batches)
    );
  }

  [Fact]
  // Fails when a press past the station's count is allowed, or another pattern type is taken for the same one.
  public void A_press_is_refused_past_the_count_and_for_another_pattern() {
    const string billets = "test:pattern-castbillets";

    Assert.Equal(
      SandStationPressOutcome.Bed,
      SandStationLogic.Press(billets, billets, 11, 12)
    );
    Assert.Equal(
      SandStationPressOutcome.Full,
      SandStationLogic.Press(billets, billets, 12, 12)
    );
    Assert.Equal(
      SandStationPressOutcome.OtherPattern,
      SandStationLogic.Press(billets, "test:pattern-castblooms", 1, 12)
    );
    Assert.Equal(
      SandStationPressOutcome.OtherPattern,
      SandStationLogic.Press(billets, "other:pattern-castbillets", 1, 12)
    );
  }

  [Fact]
  public void A_pour_below_pour_temp_is_a_misrun() {
    Assert.True(SandStationLogic.IsMisrun(pourTemp: 1000f, minPourTemp: 1150f));
  }

  [Fact]
  public void At_or_above_pour_temp_is_a_good_cast() {
    Assert.False(SandStationLogic.IsMisrun(1200f, 1150f));
    Assert.False(SandStationLogic.IsMisrun(1150f, 1150f));
  }

  [Fact]
  public void Zero_min_pour_temp_disables_the_misrun_check() {
    Assert.False(SandStationLogic.IsMisrun(20f, 0f));
  }

  [Fact]
  public void An_unrecorded_pour_temperature_is_never_a_misrun() {
    // A station with no recorded pour temperature has nothing to judge.
    Assert.False(SandStationLogic.IsMisrun(null, 1150f));
  }

  [Fact]
  public void Intake_needs_an_impression_and_an_unfilled_liquid_cavity() {
    Assert.True(
      SandStationLogic.CanIntake(
        hasImpression: true,
        cavityFull: false,
        solidified: false
      )
    );
    Assert.False(SandStationLogic.CanIntake(false, false, false)); // no impression
    Assert.False(SandStationLogic.CanIntake(true, true, false)); // already full
    Assert.False(SandStationLogic.CanIntake(true, false, true)); // solidified
  }

  [Fact]
  public void Shake_out_rakes_the_station_back_to_full_sand() {
    // Breaking the casting free wrecks the impression, not the bed.
    Assert.Equal(SandStationLevel.Full, SandStationLogic.AfterShakeOut);
  }

  [Fact]
  public void A_shaken_out_station_immediately_accepts_a_pattern_again() {
    Assert.Equal(
      SandStationAction.Imprint,
      SandStationLogic.Decide(
        holdingSand: false,
        holdingPattern: true,
        emptyHand: false,
        sand: SandStationLogic.AfterShakeOut,
        hasImpression: false,
        hasMetal: false,
        isHardened: false
      )
    );
  }

  [Fact]
  public void A_shaken_out_station_has_no_use_for_more_sand() {
    // A full station must not consume sand the player is holding for another station.
    Assert.Equal(
      SandStationAction.None,
      SandStationLogic.Decide(
        holdingSand: true,
        holdingPattern: false,
        emptyHand: false,
        sand: SandStationLogic.AfterShakeOut,
        hasImpression: false,
        hasMetal: false,
        isHardened: false
      )
    );
  }

  private static SandStationAction PatternClick(
    SandStationLevel sand,
    bool impression,
    bool closed
  ) =>
    SandStationLogic.Decide(
      holdingSand: false,
      holdingPattern: true,
      emptyHand: false,
      sand,
      impression,
      hasMetal: false,
      isHardened: false,
      closedImpression: closed
    );

  [Theory]
  [InlineData(true, SandStationLevel.Half)]
  [InlineData(false, SandStationLevel.Full)]
  // Fails when the closed press does not take Full to Half.
  public void The_closed_press_strikes_the_drag_and_the_open_press_does_not(
    bool closedPattern,
    SandStationLevel expected
  ) =>
    Assert.Equal(
      expected,
      SandStationLogic.SandAfterImprint(SandStationLevel.Full, closedPattern)
    );

  [Fact]
  // Fails when imprinting stops requiring Full sand: an open press on a Half drag would bed a piece.
  public void An_open_press_on_a_half_drag_with_no_impression_is_refused() {
    Assert.Equal(
      SandStationAction.None,
      PatternClick(SandStationLevel.Half, impression: false, closed: false)
    );
  }

  [Fact]
  // Fails when pressing again needs Full sand: the struck drag could never bed its second piece.
  public void A_pattern_presses_again_into_a_struck_drag() {
    Assert.Equal(
      SandStationAction.Press,
      PatternClick(SandStationLevel.Half, impression: true, closed: true)
    );
  }

  [Fact]
  // Fails when a Half drag with an open impression presses: only a struck drag does.
  public void A_half_drag_with_an_open_impression_does_not_press() {
    Assert.Equal(
      SandStationAction.None,
      PatternClick(SandStationLevel.Half, impression: true, closed: false)
    );
  }

  [Theory]
  [InlineData(SandStationLevel.Half)]
  [InlineData(SandStationLevel.Full)]
  // Fails when sand is rammed into a drag that holds a closed piece.
  public void Sand_into_a_drag_with_a_closed_piece_is_refused(
    SandStationLevel sand
  ) {
    Assert.Equal(
      SandStationAction.Bedded,
      SandStationLogic.Decide(
        holdingSand: true,
        holdingPattern: false,
        emptyHand: false,
        sand,
        hasImpression: true,
        hasMetal: false,
        isHardened: false,
        closedImpression: true
      )
    );
  }

  private const string ClosedFilling = "test:casting/closed-drag";

  private static readonly CastingStationKind Kind = new(
    "testflask",
    [MouldKind.Open, MouldKind.Closed],
    "test:casting/base",
    "test:casting/half",
    "test:greensand"
  );

  private static readonly CastingStationKind OtherKind = Kind with {
    SandBase = "test:other/base",
    SandHalf = "test:other/half",
  };

  [Fact]
  public void A_bare_station_shows_no_sand() {
    Assert.Null(
      SandStationLogic.FillingShape(Kind, SandStationLevel.Empty, false, null)
    );
    Assert.Null(
      SandStationLogic.FillingShape(
        Kind,
        SandStationLevel.Empty,
        true,
        "test:casting/plate"
      )
    );
  }

  // Fails when the base and half meshes are swapped, or the kind's meshes are ignored.
  [Fact]
  public void Rammed_sand_shows_the_kinds_own_base_and_half() {
    Assert.Equal(
      "test:casting/base",
      SandStationLogic.FillingShape(Kind, SandStationLevel.Full, false, null)
    );
    Assert.Equal(
      "test:casting/half",
      SandStationLogic.FillingShape(Kind, SandStationLevel.Half, false, null)
    );
    Assert.Equal(
      "test:other/base",
      SandStationLogic.FillingShape(
        OtherKind,
        SandStationLevel.Full,
        false,
        null
      )
    );
  }

  // Fails when an impression stops replacing the base, or an unresolved one stops falling back to it.
  [Fact]
  public void An_impressed_station_shows_the_patterns_cavity_or_the_base() {
    Assert.Equal(
      "test:casting/plate",
      SandStationLogic.FillingShape(
        Kind,
        SandStationLevel.Full,
        true,
        "test:casting/plate"
      )
    );
    Assert.Equal(
      "test:casting/base",
      SandStationLogic.FillingShape(Kind, SandStationLevel.Full, true, null)
    );
  }

  [Theory]
  [InlineData(true, ClosedFilling)]
  [InlineData(false, "test:casting/half")]
  // Fails when the closed branch returns the half shape, or the half branch returns the impression's.
  public void The_struck_drag_draws_the_closed_filling_and_a_plain_half_the_half_shape(
    bool closedImpression,
    string expected
  ) =>
    Assert.Equal(
      expected,
      SandStationLogic.FillingShape(
        Kind,
        SandStationLevel.Half,
        closedImpression,
        ClosedFilling,
        closedImpression
      )
    );

  [Fact]
  public void The_mesh_cache_key_differs_per_pressed_count() {
    Assert.NotEqual(
      SandStationLogic.FillingMeshKey("test:casting/x", 1),
      SandStationLogic.FillingMeshKey("test:casting/x", 2)
    );
  }

  private static ShapeElement Group(string name, params ShapeElement[] kids) =>
    new() { Name = name, Children = kids };

  // Fails when a core is kept for an unpressed piece, or a pressed piece keeps its level sand.
  [Fact]
  public void A_pressed_piece_keeps_its_bedded_group_and_the_rest_stay_level() {
    var shape = new Shape {
      Elements =
      [
        Group(
          "Piece1",
          Group("Bedded", new ShapeElement { Name = "Core1" }),
          Group("Level", new ShapeElement { Name = "Sand1" })
        ),
        Group(
          "Piece2",
          Group("Bedded", new ShapeElement { Name = "Core2" }),
          Group("Level", new ShapeElement { Name = "Sand1" })
        ),
        new ShapeElement { Name = "Runner" },
      ],
    };

    string[] paths =
    [
      .. ExShapeElements.AllPaths(SandStationLogic.PrunedToPressed(shape, 1)),
    ];

    Assert.Contains("Piece1/Bedded/Core1", paths);
    Assert.DoesNotContain("Piece2/Bedded/Core2", paths);
    Assert.Contains("Piece2/Level/Sand1", paths);
    Assert.DoesNotContain("Piece1/Level/Sand1", paths);
    Assert.Contains("Runner", paths);
  }

  [Fact]
  public void A_shape_without_piece_groups_is_returned_as_it_is() {
    var shape = new Shape { Elements = [new ShapeElement { Name = "Sand" }] };

    Assert.Same(shape, SandStationLogic.PrunedToPressed(shape, 1));
  }

  // Fails when the share is not compared to the pressed pieces' units.
  [Theory]
  [InlineData(80, 2, 10, true)]
  [InlineData(80, 2, 0, false)]
  [InlineData(40, 2, 10, false)]
  public void The_head_stands_in_the_runner_box_once_the_pieces_are_full(
    int amount,
    int pressed,
    int share,
    bool stands
  ) {
    var spec = new MoldSpec(
      1,
      "testflask",
      "test:casting/x",
      2,
      40,
      [],
      default,
      new JsonItemStack(),
      0f,
      Share: share
    );

    Assert.Equal(stands, SandStationLogic.HeadStands(spec, pressed, amount));
    Assert.False(SandStationLogic.HeadStands(null, pressed, amount));
  }
}
