#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What the <c>interact</c>, <c>ignite</c> and <c>window</c> steps do to a block, its entity
/// and the player, and what fails them (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupHoldTests
{
  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupHold"
  );

  private const string Dial =
    "{\"place\": {\"id\": \"dial\", \"code\": \"setuphold:dial\", \"at\": [0, 0, 0]}}";
  private const string Box =
    "{\"place\": {\"id\": \"box\", \"code\": \"setuphold:box\", \"at\": [1, 0, 0]}}";
  private const string Lamp =
    "{\"place\": {\"id\": \"lamp\", \"code\": \"setuphold:lamp\", \"at\": [0, 0, 0]}}";

  private static SetupScene Build(
    string steps,
    Action<TestWorld>? afterLoad = null
  ) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setuphold\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FixturePath],
      afterLoad,
      Register
    );

  internal static void Register(TestWorld w)
  {
    w.RegisterClass("SetupHoldBlock", typeof(SetupHoldBlock))
      .RegisterClass("SetupHoldEntity", typeof(SetupHoldEntity))
      .RegisterBlockEntityFactory(
        "SetupHoldEntity",
        () => new SetupHoldEntity()
      )
      .RegisterClass("SetupHoldEmber", typeof(SetupHoldEmber))
      .RegisterBlockEntityFactory("SetupHoldEmber", () => new SetupHoldEmber())
      .RegisterClass("SetupHoldBox", typeof(SetupHoldBox))
      .RegisterBlockEntityFactory("SetupHoldBox", () => new SetupHoldBox());
  }

  private static string Refused(
    string steps,
    Action<TestWorld>? afterLoad = null
  ) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(steps, afterLoad))
      .Message;

  private static SetupHoldBlock BlockAt(SetupScene scene, string id) =>
    Assert.IsType<SetupHoldBlock>(scene.World.GetBlock(scene.PositionOf(id)));

  private static SetupHoldEntity EntityAt(SetupScene scene, string id) =>
    Assert.IsType<SetupHoldEntity>(
      scene.World.GetBlockEntity(scene.PositionOf(id))
    );

  private static SetupHoldBox BoxAt(SetupScene scene, string id) =>
    Assert.IsType<SetupHoldBox>(
      scene.World.GetBlockEntity(scene.PositionOf(id))
    );

  private static List<string> HoldCalls(int steps, float stopAt)
  {
    var calls = new List<string> { "start" };
    for (int i = 1; i <= steps; i++)
      calls.Add($"step {i / 10f:0.0}");
    calls.Add($"stop {stopAt:0.0}");
    return calls;
  }

  // Fails when a hold ticks the world once per interaction step instead of once per second.
  [Fact]
  public void A_two_second_interact_hold_fires_a_listener_twice_over_twenty_steps()
  {
    using SetupScene scene = Build(
      "["
        + Dial
        + ", {\"interact\": {\"at\": {\"of\": \"dial\"}, \"seconds\": 2}}]"
    );

    Assert.Equal(HoldCalls(20, 2f), BlockAt(scene, "dial").Calls);
    Assert.Equal(2, EntityAt(scene, "dial").Fires);
  }

  // Fails when a hold runs past the step that declines, or ticks the second that step ends.
  [Fact]
  public void A_hold_whose_step_declines_stops_there_and_ticks_no_second()
  {
    using SetupScene scene = Build(
      "[{\"place\": {\"id\": \"decliner\", \"code\": \"setuphold:decliner\", \"at\": [0, 0, 0]}}, "
        + "{\"interact\": {\"at\": {\"of\": \"decliner\"}, \"seconds\": 2}}]"
    );

    Assert.Equal(HoldCalls(5, 0.5f), BlockAt(scene, "decliner").Calls);
    Assert.Equal(0, EntityAt(scene, "decliner").Fires);
  }

  // Fails when the step's returns is not compared with the answer.
  [Fact]
  public void An_interact_answer_other_than_returns_fails_the_step()
  {
    string message = Refused(
      "["
        + Dial
        + ", {\"interact\": {\"at\": {\"of\": \"dial\"}, \"returns\": false}}]"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("interact answered True, not False.", message);
  }

  // Fails when a held stack, the sneak key or the sprint key is left out of the hold.
  [Fact]
  public void An_interact_holds_its_stack_and_sneak_and_sprint_keys_and_clears_the_hand()
  {
    using SetupScene scene = Build(
      "["
        + Dial
        + ", "
        + Box
        + ", {\"interact\": {\"at\": {\"of\": \"dial\"}, \"with\": {\"stack\": \"setuphold:slab\", \"count\": 3, \"temperature\": 850}, \"keys\": [\"sneak\", \"sprint\"]}}"
        + ", {\"window\": {\"at\": {\"of\": \"box\"}, \"take\": {\"slot\": 0}}}]"
    );

    SetupHoldBlock dial = BlockAt(scene, "dial");
    Assert.Equal(
      "setuphold:slab",
      dial.HeldAtStart!.Collectible.Code.ToString()
    );
    Assert.Equal(3, dial.HeldAtStart.StackSize);
    Assert.Equal(850f, dial.HeldTemperature);
    Assert.True(dial.SneakAtStart);
    Assert.True(dial.SprintAtStart);
    Assert.False(dial.CtrlAtStart);
  }

  // Fails when the ctrl key is left out of the hold, or does not also sprint.
  [Fact]
  public void An_interact_with_the_ctrl_key_holds_ctrl_and_sprint()
  {
    using SetupScene scene = Build(
      "["
        + Dial
        + ", {\"interact\": {\"at\": {\"of\": \"dial\"}, \"keys\": [\"ctrl\"]}}]"
    );

    SetupHoldBlock dial = BlockAt(scene, "dial");
    Assert.True(dial.CtrlAtStart);
    Assert.True(dial.SprintAtStart);
    Assert.False(dial.SneakAtStart);
  }

  // Fails when an unknown key, a bad face or a negative hold is let through.
  [Theory]
  [InlineData("\"keys\": [\"jump\"]", "\"jump\" is not a key the hold holds")]
  [InlineData("\"face\": \"x\"", "\"x\" is not one of n, e, s, w, u, d.")]
  [InlineData("\"seconds\": -1", "seconds -1 is negative.")]
  public void An_interact_body_the_hold_cannot_take_fails_the_step(
    string body,
    string reason
  )
  {
    string message = Refused(
      "["
        + Dial
        + ", {\"interact\": {\"at\": {\"of\": \"dial\"}, "
        + body
        + "}}]"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains(reason, message);
  }

  // Fails when an ignite does not hold the seconds it is given, or does not tick the second it ends.
  [Fact]
  public void An_ignite_of_a_block_that_answers_ignite_now_holds_until_then()
  {
    using SetupScene scene = Build(
      "[" + Lamp + ", {\"ignite\": {\"at\": {\"of\": \"lamp\"}}}]"
    );

    SetupHoldBlock lamp = BlockAt(scene, "lamp");
    Assert.Equal(15, lamp.Tries);
    Assert.Equal([1.5f], lamp.Overs);
    Assert.Equal(1, EntityAt(scene, "lamp").Fires);
  }

  // Fails when a block that refuses the ignite is lit anyway.
  [Fact]
  public void An_ignite_of_a_block_that_refuses_fails_the_step()
  {
    SetupHoldBlock? latch = null;
    string message = Refused(
      "[{\"place\": {\"id\": \"latch\", \"code\": \"setuphold:latch\", \"at\": [0, 0, 0]}}, "
        + "{\"ignite\": {\"at\": {\"of\": \"latch\"}}}]",
      w =>
        latch = (SetupHoldBlock)
          w.World.GetBlock(new AssetLocation("setuphold", "latch"))
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("setuphold:latch at (0, 0, 0) is not ignitable.", message);
    Assert.Equal(1, latch!.Tries);
  }

  // Fails when a block with no IIgnitable is treated as lit.
  [Fact]
  public void An_ignite_of_a_block_that_is_not_ignitable_fails_the_step()
  {
    string message = Refused(
      "[" + Box + ", {\"ignite\": {\"at\": {\"of\": \"box\"}}}]"
    );

    Assert.Contains("setuphold:box at (1, 0, 0) is not ignitable.", message);
  }

  // Fails when a block not lit within the given seconds passes the step.
  [Fact]
  public void An_ignite_not_lit_within_its_seconds_fails_the_step()
  {
    string message = Refused(
      "["
        + Lamp
        + ", {\"ignite\": {\"at\": {\"of\": \"lamp\"}, \"seconds\": 0.5}}]"
    );

    Assert.Contains(
      "setuphold:lamp at (0, 0, 0) is not lit within 0.5 seconds.",
      message
    );
  }

  // Fails when an ignite looks for IIgnitable on the block class alone, so a block entity that is one
  // is not lit.
  [Fact]
  public void An_ignite_lights_a_block_whose_entity_is_ignitable()
  {
    using SetupScene scene = Build(
      "[{\"place\": {\"id\": \"ember\", \"code\": \"setuphold:ember\", \"at\": [0, 0, 0]}}, "
        + "{\"ignite\": {\"at\": {\"of\": \"ember\"}}}]"
    );

    Assert.Equal(
      [1f],
      Assert
        .IsType<SetupHoldEmber>(
          scene.World.GetBlockEntity(scene.PositionOf("ember"))
        )
        .Overs
    );
  }

  // Fails when an ignite holds the game's ignite on a block whose placer answers it.
  [Fact]
  public void An_ignite_of_a_block_whose_placer_lights_it_acts_at_once()
  {
    var placer = new IgnitingPlacer();
    SetupPlacers.Register(placer);
    try
    {
      using SetupScene scene = Build(
        "[{\"place\": {\"id\": \"slab\", \"code\": \"setuphold:slab\", \"at\": [2, 0, 0]}}, "
          + "{\"ignite\": {\"at\": {\"of\": \"slab\"}}}]"
      );

      Assert.Equal([scene.PositionOf("slab")], placer.Lit);
    }
    finally
    {
      SetupPlacers.Clear();
    }
  }

  private sealed class IgnitingPlacer : ISetupPlacer
  {
    public readonly List<BlockPos> Lit = [];

    public bool Takes(string code) => code == "setuphold:slab";

    public string? StandIn(string code) => null;

    public void Place(SetupScene scene, Block block, BlockPos pos) =>
      scene.World.Accessor.SetBlock(block.BlockId, pos);

    public bool Ignite(SetupScene scene, BlockPos pos)
    {
      Lit.Add(pos.Copy());
      return true;
    }
  }

  // Fails when a window skips the open, put, take or close packet.
  [Fact]
  public void A_window_sends_open_put_take_and_close_packets_in_order()
  {
    using SetupScene scene = Build(
      "["
        + Box
        + ", {\"window\": {\"at\": {\"of\": \"box\"}, \"open\": true, "
        + "\"put\": {\"slot\": 0, \"stack\": \"setuphold:slab\", \"count\": 3}, "
        + "\"take\": {\"slot\": 0}, \"close\": true}}]"
    );

    Assert.Equal(
      new List<(int, int)> { (1000, 0), (7, 3), (7, 0), (1001, 0) },
      BoxAt(scene, "box").Packets.Select(p => (p.Packet, p.Slot0)).ToList()
    );
  }

  // Fails when a press drops its data, or sends it as other bytes.
  [Fact]
  public void A_window_press_sends_its_id_and_data_bytes()
  {
    using SetupScene scene = Build(
      "["
        + Box
        + ", {\"window\": {\"at\": {\"of\": \"box\"}, \"press\": {\"id\": 1005, \"data\": [1, 255]}}}]"
    );

    (int packet, byte[]? data, _) = Assert.Single(BoxAt(scene, "box").Packets);
    Assert.Equal(1005, packet);
    Assert.Equal(new byte[] { 1, 255 }, data);
  }

  // Fails when a press id that is a slot, open or close packet, a byte outside 0 to 255, a slot range
  // that runs backwards, or a put without a slot is let through.
  [Theory]
  [InlineData(
    "\"press\": {\"id\": 1001}",
    "press id 1001 is a slot, open or close packet"
  )]
  [InlineData(
    "\"press\": {\"id\": 1005, \"data\": [256]}",
    "256 is not a byte, 0 to 255."
  )]
  [InlineData(
    "\"put\": {\"slot\": [1, 0], \"stack\": \"setuphold:slab\", \"count\": 1}",
    "slot range [1, 0] runs backwards."
  )]
  [InlineData(
    "\"put\": {\"stack\": \"setuphold:slab\", \"count\": 1}",
    "put needs a slot."
  )]
  public void A_window_body_the_window_cannot_send_fails_the_step(
    string body,
    string reason
  )
  {
    string message = Refused(
      "[" + Box + ", {\"window\": {\"at\": {\"of\": \"box\"}, " + body + "}}]"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains(reason, message);
  }

  // Fails when a take into a hand still holding a stack is let through; a put of 2 onto a full slot of 4 swaps.
  [Fact]
  public void A_window_take_into_a_hand_that_holds_a_stack_fails_the_step()
  {
    string message = Refused(
      "["
        + Box
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setuphold:slab\", \"count\": 4, \"slot\": 0}}"
        + ", {\"window\": {\"at\": {\"of\": \"box\"}, \"put\": {\"slot\": 0, \"stack\": \"setuphold:slab\", \"count\": 2}, \"take\": {\"slot\": 0}}}]"
    );

    Assert.Contains("steps[2]", message);
    Assert.Contains(
      "the hand holds 4 setuphold:slab; a take needs it empty.",
      message
    );
  }

  // Fails when a window put at an entity with no inventory is let through.
  [Fact]
  public void A_window_at_an_entity_with_no_inventory_fails_the_step()
  {
    string message = Refused(
      "["
        + Dial
        + ", {\"window\": {\"at\": {\"of\": \"dial\"}, \"put\": {\"slot\": 0, \"stack\": \"setuphold:slab\", \"count\": 1}}}]"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("SetupHoldEntity has no inventory to click", message);
  }
}
#endif
