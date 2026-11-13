#if GAME_GE_1_22
using System;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A stack a block gives the player through <c>TryGiveItemstack</c>: where it lands, what it carries and
/// that a following click lays it.</summary>
[Collection("Setups")]
public class SetupGiveTests
{
  private const string Tongs =
    ", \"offHand\": {\"stack\": \"setuphold:tongs\"}";

  private static string Giver(string code) =>
    "{\"place\": {\"id\": \"giver\", \"code\": \"setuphold:"
    + code
    + "\", \"at\": [0, 0, 0]}}";

  private static string Click(string extra = "") =>
    "{\"interact\": {\"at\": {\"of\": \"giver\"}, \"keep\": true"
    + extra
    + "}}";

  private static string Select(int slot, string extra = "") =>
    "{\"select\": {\"slot\": " + slot + extra + "}}";

  private static SetupHoldGiver GiverAt(SetupScene scene) =>
    Assert.IsType<SetupHoldGiver>(
      scene.World.GetBlock(scene.PositionOf("giver"))
    );

  // Fails when the give drops the stack to the ground, or empties the active slot of it.
  [Fact]
  public void A_hot_stack_given_with_tongs_in_the_off_hand_stays_in_the_active_slot()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "[" + Giver("giverhot") + ", " + Click(Tongs) + "]"
    );

    ItemStack held = scene.Player.ActiveSlot.Itemstack!;
    Assert.Equal("setuphold:wand", held.Collectible.Code.ToString());
    Assert.Equal(
      900f,
      held.Collectible.GetTemperature(scene.World.World, held)
    );
    Assert.Equal(0, GiverAt(scene).Spilled);
  }

  // Fails when the hot stack loses its temperature on the way into the slot, or the next click does not lay it.
  [Fact]
  public void A_following_click_lays_the_hot_stack_with_its_temperature()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "[" + Giver("giverhot") + ", " + Click(Tongs) + ", " + Click() + "]"
    );

    SetupHoldGiver giver = GiverAt(scene);
    Assert.Equal(1, giver.Laid);
    Assert.Equal(900f, giver.LaidTemperature);
    Assert.Null(scene.Player.ActiveSlot.Itemstack);
  }

  // Fails when a hot stack given to a bare hand is kept, which no player's hand does.
  [Fact]
  public void A_hot_stack_given_to_a_bare_hand_spills_and_is_not_held()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "[" + Giver("giverhot") + ", " + Click() + "]"
    );

    Assert.Null(scene.Player.ActiveSlot.Itemstack);
  }

  // Fails when a cold stack does not reach the active slot, or the next click does not lay it.
  [Fact]
  public void A_cold_stack_given_to_a_bare_hand_is_held_and_a_following_click_lays_it()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "[" + Giver("givercold") + ", " + Click() + "]"
    );

    ItemStack held = scene.Player.ActiveSlot.Itemstack!;
    Assert.Equal("setuphold:wand", held.Collectible.Code.ToString());
    Assert.Equal(0, GiverAt(scene).Spilled);

    using SetupScene second = SetupHoldTests.Build(
      "[" + Giver("givercold") + ", " + Click() + ", " + Click() + "]"
    );
    SetupHoldGiver giver = GiverAt(second);
    Assert.Equal(1, giver.Laid);
    Assert.Equal(20f, giver.LaidTemperature);
  }

  // Fails when the off hand a select is given is not in hand for the slot change, so the hot stack drops on the way back.
  [Fact]
  public void Tongs_given_to_each_select_carry_the_hot_stack_to_another_slot_and_back()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "["
        + Giver("giverhot")
        + ", "
        + Click(Tongs)
        + ", "
        + Select(1, Tongs)
        + ", "
        + Select(0, Tongs)
        + ", "
        + Click()
        + "]"
    );

    SetupHoldGiver giver = GiverAt(scene);
    Assert.Equal(1, giver.Laid);
    Assert.Equal(900f, giver.LaidTemperature);
  }

  // Fails when a hot stack survives a select back to its slot with nothing heatResistant in the off hand.
  [Fact]
  public void A_hot_stack_drops_on_a_select_back_to_its_slot_without_tongs()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "["
        + Giver("giverhot")
        + ", "
        + Click(Tongs)
        + ", "
        + Select(1)
        + ", "
        + Select(0)
        + "]"
    );

    Assert.Null(scene.Player.ActiveSlot.Itemstack);
    Assert.Equal(0, GiverAt(scene).Laid);
  }

  // Fails when the off hand a select was given stays in hand after the step.
  [Fact]
  public void An_off_hand_given_to_a_select_is_gone_after_the_step()
  {
    using SetupScene scene = SetupHoldTests.Build("[" + Select(1, Tongs) + "]");

    Assert.Null(scene.Player.OffHandSlot.Itemstack);
  }

  private static string SelectCode(string code, string extra = "") =>
    "{\"select\": {\"code\": \"" + code + "\"" + extra + "}}";

  // Fails when a select by code takes the first slot or the active one instead of the slot that holds the item.
  [Fact]
  public void A_select_by_code_makes_the_slot_holding_the_item_active()
  {
    using SetupScene scene = SetupHoldTests.Build(
      "[{\"place\": {\"id\": \"dial\", \"code\": \"setuphold:dial\", \"at\": [0, 0, 0]}}"
        + ", {\"fillInventory\": {\"code\": \"setuphold:tongs\"}}, "
        + Select(3)
        + ", {\"interact\": {\"at\": {\"of\": \"dial\"}, \"with\": {\"stack\": \"setuphold:wand\"}, \"keep\": true}}, "
        + Select(0)
        + ", "
        + SelectCode("setuphold:wand")
        + "]"
    );

    Assert.Equal(3, scene.Player.ActiveNumber);
    Assert.Equal(
      "setuphold:wand",
      scene.Player.ActiveSlot.Itemstack!.Collectible.Code.ToString()
    );
  }

  // Fails when a select by code with no slot holding the item passes.
  [Fact]
  public void A_select_by_code_of_an_item_no_slot_holds_fails_the_step()
  {
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        SetupHoldTests.Build("[" + SelectCode("setuphold:wand") + "]")
    );

    Assert.Contains("no hotbar slot holds setuphold:wand", error.Message);
  }

  // Fails when a select with both a slot and a code, or with neither, is accepted.
  [Theory]
  [InlineData("{\"slot\": 1, \"code\": \"setuphold:wand\"}")]
  [InlineData("{}")]
  public void A_select_takes_one_of_slot_and_code(string body)
  {
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        SetupHoldTests.Build("[{\"select\": " + body + "}]")
    );

    Assert.Contains("takes one of \"slot\" and \"code\"", error.Message);
  }
}
#endif
