#if GAME_GE_1_22
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
}
#endif
