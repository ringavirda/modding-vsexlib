using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="BlockBehaviorExOrientable"/>, the family's replacement for vanilla's
/// <c>HorizontalOrientable</c>: horizontal and omni placement, the space check a placement passes, the
/// canonical drop and pick stack, and <c>ApplyOrientation</c>.
/// </summary>
public class ExOrientableTests {
  private static readonly string[] FourSides = ["n", "e", "s", "w"];

  #region Horizontal placement

  [Theory]
  [InlineData("north", "n")]
  [InlineData("east", "e")]
  [InlineData("south", "s")]
  [InlineData("west", "w")]
  public void A_placed_block_wears_the_side_the_player_looked_towards(
    string look,
    string token
  ) {
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);

    Assert.True(rig.PlaceLooking(look));

    Assert.Equal($"exlib:probe-{token}", rig.PlacedCode);
  }

  [Fact]
  public void A_multi_segment_code_keeps_every_segment_before_the_side() {
    // CodeWithParts(facing) replaces only the first dash-segment; resolving by variant name does not.
    var rig = ExOrientableRig.WithVariants(
      "exlib:crafting",
      "side",
      FourSides,
      fixedGroups: ("kind", "designtable")
    );

    Assert.True(rig.PlaceLooking("west"));

    Assert.Equal("exlib:crafting-designtable-w", rig.PlacedCode);
  }

  // Fails when the placed code is built from the code's first dash-segment only.
  [Theory]
  [InlineData("north", "n")]
  [InlineData("east", "e")]
  [InlineData("south", "s")]
  [InlineData("west", "w")]
  public void A_code_with_a_dash_places_in_every_facing(
    string look,
    string token
  ) {
    var rig = ExOrientableRig.WithVariants(
      "exlib:crafting-workbench",
      "side",
      FourSides
    );

    Assert.True(rig.PlaceLooking(look));

    Assert.Equal($"exlib:crafting-workbench-{token}", rig.PlacedCode);
  }

  [Fact]
  public void A_missing_variant_state_is_refused_and_logged_rather_than_crashing() {
    // A missing variant state resolves to no block: placement is refused and logged, not dereferenced.
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", ["n"]);
    rig.World.Log.Expect(EnumLogType.Error, "no 'side' state 'w'");

    bool placed = rig.PlaceLooking("west");

    Assert.False(placed);
    Assert.Equal("cantplace", rig.FailureCode);
    Assert.Contains("no 'side' state 'w'", Assert.Single(rig.LoggedErrors));
    Assert.Null(rig.PlacedCode);
  }

  #endregion

  #region Checking the space

  // Fails when TryPlaceBlock skips the oriented block's CanPlaceBlock.
  [Fact]
  public void A_cell_holding_a_block_that_cannot_be_replaced_is_refused() {
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);
    rig.Occupy(ExOrientableRig.Pos);

    Assert.False(rig.PlaceLooking("west"));

    Assert.Equal("notreplaceable", rig.FailureCode);
    Assert.Equal("game:rock-granite", rig.PlacedCode);
  }

  // Fails when TryPlaceBlock skips CanPlaceBlock: only the megablock's footprint check refuses here.
  [Fact]
  public void A_megablock_with_a_footprint_cell_taken_is_refused() {
    var rig = ExOrientableRig.Megablock("""[{ "x": 0, "y": 1, "z": 0 }]""");
    rig.Occupy(ExOrientableRig.Pos.UpCopy());

    Assert.False(rig.PlaceLooking("west"));

    Assert.Equal("notenoughspace", rig.FailureCode);
    Assert.Null(rig.PlacedCode);
  }

  // Fails when TryPlaceBlock skips CanPlaceBlock: only the behaviour's override refuses here.
  [Fact]
  public void A_behaviour_that_refuses_the_placement_is_obeyed_with_its_code() {
    var rig = ExOrientableRig
      .WithVariants("exlib:probe", "side", FourSides)
      .WithBehaviour(b => new RefusesPlacement(b));

    Assert.False(rig.PlaceLooking("west"));

    Assert.Equal("refusedbytest", rig.FailureCode);
    Assert.Null(rig.PlacedCode);
  }

  // Fails when TryPlaceBlock skips CanPlaceBlock: only the land-claim test refuses here.
  [Fact]
  public void A_claimed_cell_is_refused_to_a_player_without_access() {
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);
    rig.World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        ExOrientableRig.Pos,
        EnumBlockAccessFlags.BuildOrBreak
      )
      .Returns(false);

    Assert.False(rig.PlaceLooking("west"));

    Assert.Equal("claimed", rig.FailureCode);
    Assert.Null(rig.PlacedCode);
  }

  // Fails when TryPlaceBlock sets the block itself instead of calling the oriented block's
  // DoPlaceBlock.
  [Fact]
  public void A_free_cell_takes_the_oriented_variant_through_its_place_hooks() {
    var placedAs = new List<string>();
    var rig = ExOrientableRig
      .WithVariants("exlib:probe", "side", FourSides)
      .WithBehaviour(b => new RecordsPlacement(b, placedAs));

    Assert.True(rig.PlaceLooking("west"));

    Assert.Equal("exlib:probe-w", rig.PlacedCode);
    Assert.Equal(["exlib:probe-w"], placedAs);
  }

  private sealed class RefusesPlacement(Block block) : BlockBehavior(block) {
    public override bool CanPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel,
      ref EnumHandling handling,
      ref string failureCode
    ) {
      handling = EnumHandling.PreventDefault;
      failureCode = "refusedbytest";
      return false;
    }
  }

  private sealed class RecordsPlacement(Block block, List<string> placedAs)
    : BlockBehavior(block) {
    public override bool DoPlaceBlock(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel,
      ItemStack byItemStack,
      ref EnumHandling handling
    ) {
      placedAs.Add(block.Code.ToString());
      return true;
    }
  }

  #endregion

  #region Fungibility

  [Fact]
  public void Every_facing_drops_and_picks_the_same_canonical_stack() {
    // GetDrops and OnPickBlock both answer the scheme's first token.
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);
    rig.PlaceLooking("west");

    Assert.Equal("exlib:probe-n", rig.DropCode);
    Assert.Equal("exlib:probe-n", rig.PickCode);
  }

  [Fact]
  public void The_canonical_stack_is_the_schemes_first_token_not_a_hard_coded_north() {
    // The canonical stack reads the scheme's first token, not a hard-coded north.
    var rig = ExOrientableRig.WithVariants(
      "exlib:axle",
      "orientation",
      ["ns", "we"],
      mode: "network",
      scheme: "Axis"
    );

    Assert.Equal("exlib:axle-ns", rig.DropCode);
    Assert.Equal("exlib:axle-ns", rig.PickCode);
  }

  #endregion

  #region Modes

  [Fact]
  public void A_network_oriented_block_leaves_placement_to_its_own_connector_scan() {
    // A network block keeps whatever orientation the stack carried; the node re-orients it on the
    // next neighbour notification.
    var rig = ExOrientableRig.WithVariants(
      "exlib:axle",
      "orientation",
      ["ns", "we"],
      mode: "network",
      scheme: "Axis"
    );

    Assert.True(rig.PlaceLooking("west"));

    // Handled by the engine's default path; the rig's own store sees no SetBlock.
    Assert.Null(rig.PlacedCode);
    Assert.Equal("orientation", rig.VariantKey);
  }

  [Fact]
  public void An_omni_block_clicked_on_a_wall_still_takes_the_horizontal_look() {
    var rig = OmniProbe();

    // A horizontal selected face: the player clicked the side of a neighbour.
    Assert.True(rig.PlaceLooking("south", selectedFace: "north"));

    Assert.Equal("exlib:probe-s", rig.PlacedCode);
  }

  [Theory]
  [InlineData("up", "u")]
  [InlineData("down", "d")]
  public void An_omni_block_clicked_on_a_floor_or_ceiling_points_that_way(
    string selectedFace,
    string token
  ) {
    // No block declares mode "omni"; this is TokenFor's only coverage of the vertical arm.
    var rig = OmniProbe();

    Assert.True(rig.PlaceLooking("south", selectedFace));

    Assert.Equal($"exlib:probe-{token}", rig.PlacedCode);
  }

  [Fact]
  public void A_network_block_that_names_no_scheme_falls_back_silently() {
    // The unresolved scheme name is recorded for a misspelled JSON asset.
    var rig = ExOrientableRig.WithVariants(
      "exlib:axle",
      "orientation",
      ["ns", "we", "ud"],
      mode: "network"
    );

    Assert.Equal(ExOrientations.Axis.Name, rig.Scheme.Name);
    Assert.Null(rig.UnresolvedScheme);
  }

  [Fact]
  public void A_network_block_that_names_a_scheme_wrongly_records_the_name() {
    // NetworkOriented writes the resolved name from the block's own states; this is the
    // JSON-authored case.
    var rig = ExOrientableRig.WithVariants(
      "exlib:bend",
      "orientation",
      ["nw", "se", "en", "ws"],
      mode: "network",
      scheme: "CanalBnd"
    );

    Assert.Equal(ExOrientations.Axis.Name, rig.Scheme.Name);
    Assert.Equal("CanalBnd", rig.UnresolvedScheme);
    Assert.False(rig.ApplyOrientation("se"));
  }

  private static ExOrientableRig OmniProbe() =>
    ExOrientableRig.WithVariants(
      "exlib:probe",
      "side",
      ["n", "e", "s", "w", "u", "d"],
      mode: "omni"
    );

  #endregion

  #region The shared mechanism

  [Fact]
  public void ApplyOrientation_swaps_a_placed_block_to_the_requested_token() {
    // Both routes reach this: the player route via TryPlaceBlock, the network route directly.
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);
    rig.PlaceLooking("north");

    Assert.True(rig.ApplyOrientation("e"));

    Assert.Equal("exlib:probe-e", rig.PlacedCode);
  }

  [Fact]
  public void ApplyOrientation_refuses_a_token_outside_the_declared_scheme() {
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);
    rig.PlaceLooking("north");

    // `u` is in FaceAll but not in Face; a horizontal block must refuse it.
    Assert.False(rig.ApplyOrientation("u"));

    Assert.Equal("exlib:probe-n", rig.PlacedCode);
  }

  [Fact]
  public void ApplyOrientation_is_a_no_op_when_the_block_already_wears_the_token() {
    // False is load-bearing: true re-triggers the network walk on every notification.
    var rig = ExOrientableRig.WithVariants("exlib:probe", "side", FourSides);
    rig.PlaceLooking("north");

    Assert.False(rig.ApplyOrientation("n"));

    Assert.Equal("exlib:probe-n", rig.PlacedCode);
  }

  #endregion
}
