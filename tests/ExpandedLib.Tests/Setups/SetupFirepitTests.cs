#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A setup works the game's firepit through a window and ticks it lit (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupFirepitTests
{
  private const string Place =
    "{\"fill\": {\"code\": \"game:rock-granite\", \"from\": [0, 0, 0], \"to\": [3, 0, 3]}},"
    + "{\"place\": {\"id\": \"fire\", \"code\": \"game:firepit-extinct\", \"at\": [1, 1, 1]}},";

  private static SetupScene Build(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupmetal\"], \"load\": [\"game:firepit-*\", \"game:charcoal\", \"game:redmeat-raw\", \"game:redmeat-cooked\"], \"steps\": ["
          + steps
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupMetal"
        ),
      ],
      null,
      _ => { }
    );

  private const string Fire = "{\"of\": \"fire\"}";

  // Fails when the window's route to the firepit is the old one: the player out of reach of the block, so the engine drops every slot packet.
  [Fact]
  public void A_window_puts_into_and_takes_from_the_firepits_slots()
  {
    using SetupScene scene = Build(
      Place
        + "{\"window\": {\"at\": "
        + Fire
        + ", \"open\": true, \"put\": {\"slot\": 0, \"stack\": \"game:charcoal\", \"count\": 8}}},"
        + "{\"window\": {\"at\": "
        + Fire
        + ", \"put\": {\"slot\": 1, \"stack\": \"game:redmeat-raw\", \"count\": 2}}},"
        + "{\"expect\": {\"at\": "
        + Fire
        + ", \"member\": \"fuelSlot.Itemstack.StackSize\", \"equals\": 8}},"
        + "{\"expect\": {\"at\": "
        + Fire
        + ", \"member\": \"inputSlot.Itemstack.StackSize\", \"equals\": 2}},"
        + "{\"window\": {\"at\": "
        + Fire
        + ", \"take\": {\"slot\": 1}, \"close\": true}},"
        + "{\"expect\": {\"at\": "
        + Fire
        + ", \"member\": \"inputSlot.Empty\", \"equals\": true}}"
    );
  }

  // Fails when the weather registration is dropped: the lit firepit's first tick reads a weather system that is not there.
  [Fact]
  public void A_lit_firepit_cooks_its_input_into_the_output_slot()
  {
    using SetupScene scene = Build(
      Place
        + "{\"window\": {\"at\": "
        + Fire
        + ", \"open\": true, \"put\": {\"slot\": 0, \"stack\": \"game:charcoal\", \"count\": 8}}},"
        + "{\"window\": {\"at\": "
        + Fire
        + ", \"put\": {\"slot\": 1, \"stack\": \"game:redmeat-raw\", \"count\": 1}, \"close\": true}},"
        + "{\"ignite\": {\"at\": "
        + Fire
        + "}},"
        + "{\"wait\": {\"seconds\": 120}},"
        + "{\"expect\": {\"at\": "
        + Fire
        + ", \"member\": \"outputSlot.Itemstack.Collectible.Code.Path\", \"equals\": \"redmeat-cooked\"}},"
        + "{\"expect\": {\"at\": "
        + Fire
        + ", \"member\": \"inputSlot.Empty\", \"equals\": true}}"
    );
  }

  // Fails when a smelted stack resolves whose target the setup does not load: the engine logs a warning and FailOnWarnings fails the case.
  [Fact]
  public void A_collectible_whose_smelted_target_is_not_loaded_logs_nothing()
  {
    using SetupScene scene = SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupmetal\"], \"load\": [\"game:metalplate-bismuth\"], \"steps\": [], \"seconds\": 6, \"record\": {}}",
        "t.json"
      ),
      [
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupMetal"
        ),
      ],
      null,
      _ => { }
    );
  }

  // Fails when a put into a slot the block lacks, or one the slot refuses, passes without naming the block and slot.
  [Theory]
  [InlineData(9, "game:charcoal", "slot 9 is past the inventory's 7.")]
  [InlineData(
    2,
    "game:charcoal",
    "game:firepit-extinct at (1, 1, 1) took none of the 8 game:charcoal put in slot 2."
  )]
  public void A_put_the_firepit_cannot_take_fails_naming_the_block_and_slot(
    int slot,
    string stack,
    string message
  )
  {
    var e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        Place
          + "{\"window\": {\"at\": "
          + Fire
          + ", \"open\": true, \"put\": {\"slot\": "
          + slot
          + ", \"stack\": \""
          + stack
          + "\", \"count\": 8}}}"
      )
    );
    Assert.Contains(message, e.Message);
  }
}
#endif
