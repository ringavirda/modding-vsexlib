#if GAME_GE_1_22
using System;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The <c>use</c> step: the held item's own start, steps and stop (setups.md, What the harness
/// runs).</summary>
[Collection("Setups")]
public class SetupUseTests
{
  private const string Dial =
    "{\"place\": {\"id\": \"dial\", \"code\": \"setuphold:dial\", \"at\": [0, 0, 0]}}";

  private static string Use(string body) =>
    "[" + Dial + ", {\"use\": {\"at\": {\"of\": \"dial\"}, " + body + "}}]";

  private static string Placing(string with, string id, string after = "") =>
    "["
    + Dial
    + ", {\"use\": {\"id\": \""
    + id
    + "\", \"at\": {\"of\": \"dial\"}, \"with\": {\"stack\": \""
    + with
    + "\"}, \"seconds\": 0}}"
    + after
    + "]";

  private static SetupHoldWand Wand(SetupScene scene) =>
    Assert.IsType<SetupHoldWand>(
      scene.World.World.GetItem(new AssetLocation("setuphold:wand"))
    );

  // Fails when the step loop is skipped and the stop follows the start.
  [Fact]
  public void Use_runs_the_held_items_start_steps_and_stop()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use("\"with\": {\"stack\": \"setuphold:wand\"}, \"seconds\": 1")
    );

    SetupHoldWand wand = Wand(scene);
    Assert.Equal(
      ["start", .. Enumerable.Repeat("step", 10), "stop"],
      wand.Calls
    );
    Assert.Equal(
      Enumerable.Range(1, 10).Select(i => i * 0.1f).Append(1f),
      wand.Seconds
    );
    Assert.Equal([EnumHandInteract.HeldItemInteract], wand.HandUses);
    Assert.Equal(EnumHandInteract.None, scene.Player.Entity.Controls.HandUse);
  }

  // Fails when the offHand is not put in the off-hand slot before the item's start.
  [Fact]
  public void Use_puts_the_off_hand_in_the_slot_for_the_start()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use(
        "\"with\": {\"stack\": \"setuphold:wand\"}, \"offHand\": {\"stack\": \"game:stick\"}, \"seconds\": 0"
      )
    );

    Assert.Equal(["game:stick"], Wand(scene).OffHands);
  }

  // Fails when the off-hand stack is left in the slot after the step.
  [Fact]
  public void A_use_empties_the_off_hand_after_the_click()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use(
        "\"with\": {\"stack\": \"setuphold:wand\"}, \"offHand\": {\"stack\": \"game:stick\"}, \"seconds\": 0"
      )
    );

    Assert.Null(scene.Player.OffHandSlot.Itemstack);
  }

  // Fails when the player has no eye position during the start: the item's read of the facing the player
  // suggests throws.
  [Fact]
  public void Use_gives_the_start_an_eye_to_read_the_facing_from()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use("\"with\": {\"stack\": \"setuphold:wand\"}, \"seconds\": 0")
    );

    Assert.Single(Wand(scene).Facings);
  }

  // Fails when the hit is handed to the item undivided.
  [Fact]
  public void Use_hit_is_sixteenths_of_the_cell()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use(
        "\"with\": {\"stack\": \"setuphold:wand\"}, \"hit\": [8, 16, 4], \"seconds\": 0"
      )
    );

    Vintagestory.API.MathTools.Vec3d hit = Assert.Single(Wand(scene).Hits);
    Assert.Equal((0.5, 1.0, 0.25), (hit.X, hit.Y, hit.Z));
  }

  // Fails when the Required row is dropped and a use with no cell reaches the scene.
  [Fact]
  public void A_use_without_at_is_refused()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupHoldTests.Build("[{\"use\": {\"seconds\": 1}}]")
    );

    Assert.Contains("takes \"at\"", e.Message);
  }

  // Fails when the throw on a start that leaves NotHandled is removed.
  [Fact]
  public void A_use_the_item_declines_fails_naming_the_hand_and_the_cell()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupHoldTests.Build(Use("\"seconds\": 1"))
    );

    Assert.Contains("an empty hand did not take the use at", e.Message);
    Assert.Contains("(0, 0, 0)", e.Message);
  }

  private static string HitRefusal(string hit) =>
    Assert
      .Throws<InvalidOperationException>(() =>
        SetupHoldTests.Build(Use("\"hit\": " + hit + ", \"seconds\": 0"))
      )
      .Message;

  // Fails when the array check in ValidateHit is dropped.
  [Fact]
  public void A_use_hit_that_is_not_an_array_is_refused() =>
    Assert.Contains("expected an array", HitRefusal("8"));

  // Fails when the count check in ValidateHit is dropped.
  [Fact]
  public void A_use_hit_of_two_entries_is_refused() =>
    Assert.Contains("three sixteenths", HitRefusal("[8, 16]"));

  // Fails when the range check in ValidateHit is dropped.
  [Fact]
  public void A_use_hit_entry_outside_the_cell_is_refused() =>
    Assert.Contains("outside 0 to 16", HitRefusal("[8, 17, 4]"));

  // Fails when the id is not recorded, or recorded at the aimed cell and not the placed one.
  [Fact]
  public void A_use_with_an_id_is_named_by_a_later_complete_of_at_the_placed_cell()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupHoldTests.Build(
        Placing(
          "setuphold:placer",
          "slab",
          ", {\"complete\": {\"of\": \"slab\"}}"
        )
      )
    );

    Assert.Contains(
      "cell (0, 1, 0) has no block entity to complete",
      e.Message
    );
  }

  // Fails when the id is not recorded, or recorded at the aimed cell and not the placed one.
  [Fact]
  public void A_use_with_an_id_is_the_cell_a_record_machine_reads()
  {
    SetupFile file = SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setuphold\"], \"steps\": "
        + Placing("setuphold:placer", "slab")
        + ", \"seconds\": 6, \"record\": {\"machines\": {\"slab\": {\"code\": \"setuphold:slab\", "
        + "\"series\": {\"state\": {\"block\": \"setuphold:slab\"}}}}}}",
      "t.json"
    );
    using SetupScene scene = SetupScene.Build(
      file,
      [SetupHoldTests.FixturePath],
      null,
      SetupHoldTests.Register
    );

    SetupRecording recording = scene.Run([("setuphold", "1.0.0")]);

    Assert.Equal("true", recording.ValueAt("slab", "state", 0));
    Assert.Equal(
      new Vintagestory.API.MathTools.BlockPos(0, 1, 0),
      scene.PositionOf("slab")
    );
  }

  // Fails when a use that places nothing is let pass without its id naming a cell.
  [Fact]
  public void A_use_with_an_id_that_places_nothing_fails_naming_the_id()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupHoldTests.Build(Placing("setuphold:wand", "flask"))
    );

    Assert.Contains("\"flask\" placed nothing", e.Message);
  }

  // Fails when a use without an id records a placement anyway.
  [Fact]
  public void A_use_without_an_id_records_nothing()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use("\"with\": {\"stack\": \"setuphold:placer\"}, \"seconds\": 0")
    );

    Assert.Equal(["dial"], scene.Placements.Keys);
  }

  private static System.Collections.Generic.List<(
    Vintagestory.API.MathTools.BlockPos Pos,
    bool Entity,
    bool Host
  )> Cells(int count, params int[] hosts) =>
    [
      .. Enumerable
        .Range(0, count)
        .Select(i =>
          (
            new Vintagestory.API.MathTools.BlockPos(i, 1, 0),
            true,
            hosts.Contains(i)
          )
        ),
    ];

  // Fails when the host rule is dropped: 24 cells with an entity each are then ambiguous.
  [Fact]
  public void A_use_placing_a_multiblock_of_entity_cells_is_named_by_its_host()
  {
    Vintagestory.API.MathTools.BlockPos? placed = SetupUsePlaced.Of(
      Cells(24, 7),
      "bed",
      out string? problem
    );

    Assert.Null(problem);
    Assert.Equal(new Vintagestory.API.MathTools.BlockPos(7, 1, 0), placed);
  }

  // Fails when the first host is taken instead of refusing two.
  [Fact]
  public void A_use_placing_two_hosts_stays_ambiguous()
  {
    Vintagestory.API.MathTools.BlockPos? placed = SetupUsePlaced.Of(
      Cells(24, 3, 7),
      "bed",
      out string? problem
    );

    Assert.Null(placed);
    Assert.Contains("\"bed\" is ambiguous", problem);
    Assert.Contains("2 multiblock hosts", problem);
  }

  // Fails when the single block entity is no longer taken without a host.
  [Fact]
  public void A_use_placing_blocks_with_one_entity_and_no_host_is_named_by_the_entity()
  {
    var cells = Cells(3);
    cells[1] = (cells[1].Pos, false, false);
    cells[2] = (cells[2].Pos, false, false);

    Vintagestory.API.MathTools.BlockPos? placed = SetupUsePlaced.Of(
      cells,
      "bed",
      out _
    );

    Assert.Equal(cells[0].Pos, placed);
  }
}
#endif
