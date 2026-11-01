using System.Linq;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The scenario player's held verbs: the hold, its keys, the window's packets and the ignition
/// verdicts (scenarios.md, The scenario player).</summary>
public class ScenarioPlayerVerbTests {
  /// <summary>Fails when an interact step drops its control key, so every click lands on the body.</summary>
  [Fact]
  public void An_interact_step_names_its_control_and_none_without_one() {
    Assert.Equal(
      "handle",
      InteractStep.ControlOf(
        JObject.Parse("{\"at\": [0, 0, 0], \"control\": \"handle\"}")
      )
    );
    Assert.Null(InteractStep.ControlOf(JObject.Parse("{\"at\": [0, 0, 0]}")));
  }

  private sealed class Panel : Block {
    public override Cuboidf[] GetSelectionBoxes(
      IBlockAccessor blockAccessor,
      BlockPos pos
    ) =>
      [
        new CuboidfWithId(new Cuboidf(0.25f, 0.5f, 0.25f, 0.75f, 1f, 0.5f))
        {
          Id = "handle",
        },
        new Cuboidf(0f, 0f, 0f, 1f, 0.5f, 1f),
      ];
  }

  private static (TestWorld, BlockPos) PanelWorld() {
    var world = new TestWorld();
    var at = new BlockPos(0, 1, 0);
    world.Place(at, TestBlocks.Configure(new Panel(), "test:panel", 1));
    return (world, at);
  }

  /// <summary>Fails when an interact step with a control key selects the body, or the control's box is
  /// aimed at anywhere but inside it.</summary>
  [Fact]
  public void An_interact_step_with_a_control_selects_its_box() {
    var (world, at) = PanelWorld();

    BlockSelection selection = InteractStep.SelectionFor(
      world.Accessor,
      at,
      "handle",
      BlockFacing.UP
    );

    Assert.Equal("handle", selection.SelectionBoxId);
    Assert.Equal(0, selection.SelectionBoxIndex);
    Assert.Equal(new Vec3d(0.5, 1.0, 0.375), selection.HitPosition);
  }

  /// <summary>Fails when an interact step with no control key selects a box, or aims anywhere but the
  /// middle of the face.</summary>
  [Fact]
  public void An_interact_step_without_a_control_selects_the_body() {
    var (world, at) = PanelWorld();

    BlockSelection selection = InteractStep.SelectionFor(
      world.Accessor,
      at,
      null,
      BlockFacing.SOUTH
    );

    Assert.Null(selection.SelectionBoxId);
    Assert.Equal(BlockFacing.SOUTH, selection.Face);
    Assert.Equal(new Vec3d(0.5, 0.5, 1.0), selection.HitPosition);
    Assert.Equal(at, selection.Position);
  }

  /// <summary>Fails when an unknown control id escapes as an argument exception, or fails the step without
  /// naming the controls the cell has or blaming the scenario.</summary>
  [Fact]
  public void An_interact_step_naming_an_unknown_control_fails_the_step() {
    var (world, at) = PanelWorld();

    var e = Assert.Throws<StepFailure>(() =>
      InteractStep.SelectionFor(world.Accessor, at, "lever", BlockFacing.UP)
    );

    Assert.Contains("has no control \"lever\"", e.Message);
    Assert.Contains("its controls are handle.", e.Message);
    Assert.Equal(Suspect.Scenario, e.Suspect);
  }

  /// <summary>Fails when a hold runs past its seconds (S27).</summary>
  [Fact]
  public void A_hold_goes_on_while_the_time_held_is_under_its_seconds() {
    Assert.True(InteractStep.StillHeld(0.9, 1));
    Assert.False(InteractStep.StillHeld(1.0, 1));
  }

  /// <summary>Fails when a block that stops the hold keeps being stepped (S60), or a hold goes on past its
  /// seconds (S61).</summary>
  [Fact]
  public void A_hold_ends_when_a_step_stops_it_or_its_seconds_pass() {
    Assert.True(InteractStep.Goes(true, 0.5, 1));
    Assert.False(InteractStep.Goes(false, 0.5, 1));
    Assert.False(InteractStep.Goes(true, 1, 1));
  }

  private sealed class Spender : Item {
    public int Steps;
    public int Stops;

    public override bool OnHeldInteractStep(
      float secondsUsed,
      ItemSlot slot,
      EntityAgent byEntity,
      BlockSelection blockSel,
      EntitySelection entitySel
    ) {
      Steps++;
      slot.Itemstack = null;
      return true;
    }

    public override void OnHeldInteractStop(
      float secondsUsed,
      ItemSlot slot,
      EntityAgent byEntity,
      BlockSelection blockSel,
      EntitySelection entitySel
    ) => Stops++;
  }

  private static EntityAgent Agent() {
    var agent = new EntityAgent();
    agent.Controls.HandUse = EnumHandInteract.HeldItemInteract;
    return agent;
  }

  private static (Spender, ItemSlot) SpenderSlot() {
    var item = new Spender { Code = new AssetLocation("test:spender") };
    return (item, new DummySlot(new ItemStack(item, 1)));
  }

  /// <summary>Fails when a held use that spent the slot's last item still gets its stop call (a
  /// NullReferenceException on the emptied slot), or a stop on a held stack is skipped.</summary>
  [Fact]
  public void A_held_use_that_spent_the_last_item_ends_with_no_stop_call() {
    var (item, slot) = SpenderSlot();
    var selection = new BlockSelection();

    Assert.True(ScenarioPlayer.StepHeld(slot, Agent(), selection, 0.5));
    Assert.Null(slot.Itemstack);
    Assert.False(ScenarioPlayer.StopHeld(slot, Agent(), selection, 0.5));
    Assert.Equal(0, item.Stops);

    var (kept, held) = SpenderSlot();
    Assert.True(ScenarioPlayer.StopHeld(held, Agent(), selection, 0.5));
    Assert.Equal(1, kept.Stops);
  }

  /// <summary>Fails when an emptied slot is stepped again.</summary>
  [Fact]
  public void An_emptied_slot_is_not_stepped_and_ends_the_use() {
    var (item, slot) = SpenderSlot();
    var selection = new BlockSelection();
    ScenarioPlayer.StepHeld(slot, Agent(), selection, 0.5);

    Assert.False(ScenarioPlayer.StepHeld(slot, Agent(), selection, 1.0));
    Assert.Equal(1, item.Steps);
  }

  /// <summary>Fails when a use the claims refuse passes (S62), or the refusal drops the cell (S63).</summary>
  [Fact]
  public void A_use_the_claims_refuse_fails_the_step_naming_the_cell() {
    InteractStep.RequireUse(true, new BlockPos(1, 2, 3));
    var e = Assert.Throws<StepFailure>(() =>
      InteractStep.RequireUse(false, new BlockPos(1, 2, 3))
    );
    Assert.Contains("the player may not use", e.Message);
    Assert.Contains("1, 2, 3", e.Message);
  }

  /// <summary>Fails when a take of a range or a name is read as an index (S64), or an index is read as the
  /// next slot (S65).</summary>
  [Fact]
  public void A_take_names_one_slot_index() {
    Assert.Equal(2, WindowStep.TakeSlot(JObject.Parse("{\"slot\":2}")));
    foreach (
      string body in new[] { "{}", "{\"slot\":[0,1]}", "{\"slot\":\"0\"}" }
    ) {
      var e = Assert.Throws<StepFailure>(() =>
        WindowStep.TakeSlot(JObject.Parse(body))
      );
      Assert.Equal("\"take\" takes one \"slot\" index.", e.Message);
    }
  }

  /// <summary>Fails when an ignition without seconds is held for other than 5 s (S66, S68).</summary>
  [Fact]
  public void An_ignite_without_seconds_is_held_for_five() {
    Assert.Equal(5, IgniteStep.DefaultSeconds);
    Assert.Equal(5, IgniteStep.Seconds(new JObject()));
    Assert.Equal(2, IgniteStep.Seconds(new JObject { ["seconds"] = 2 }));
  }

  /// <summary>Fails when a key sets the wrong control (S28), or a control is left set by a key that is
  /// not held (S29).</summary>
  [Fact]
  public void Each_key_holds_its_own_control_and_no_other() {
    var controls = new EntityControls();
    ScenarioPlayer.Hold(controls, ["ctrl"]);
    Assert.Equal(
      (false, false, true, true),
      (controls.Sneak, controls.ShiftKey, controls.CtrlKey, controls.Sprint)
    );
    ScenarioPlayer.Hold(controls, ["sprint", "sneak"]);
    Assert.Equal(
      (true, true, false, true),
      (controls.Sneak, controls.ShiftKey, controls.CtrlKey, controls.Sprint)
    );
  }

  /// <summary>Fails when the keep key is not read, so the hand empties after a kept step.</summary>
  [Fact]
  public void An_interact_step_keeps_the_hand_only_when_it_says_keep() {
    Assert.True(
      InteractStep.Keeps(JObject.Parse("{\"at\": [0, 0, 0], \"keep\": true}"))
    );
    Assert.False(
      InteractStep.Keeps(JObject.Parse("{\"at\": [0, 0, 0], \"keep\": false}"))
    );
    Assert.False(InteractStep.Keeps(JObject.Parse("{\"at\": [0, 0, 0]}")));
  }

  /// <summary>Fails when a release empties the hand of a kept step, or leaves a stack in the hand of one that
  /// is not kept.</summary>
  [Fact]
  public void A_release_empties_the_hand_unless_the_step_kept_it() {
    var stack = new ItemStack(new Item(), 2);

    Assert.Same(stack, ScenarioPlayer.HandAfter(stack, keep: true));
    Assert.Null(ScenarioPlayer.HandAfter(stack, keep: false));
  }

  /// <summary>Fails when the case's end leaves a stack in any hotbar slot the active one is not, so the next case's
  /// lift into slot 1 meets a held item.</summary>
  [Fact]
  public void Emptying_the_slots_leaves_none_holding_a_stack() {
    var item = new Item();
    ItemSlot[] slots =
    [
      new DummySlot(new ItemStack(item, 1)),
      new DummySlot(),
      new DummySlot(new ItemStack(item, 8)),
    ];

    ScenarioPlayer.EmptySlots(slots);

    Assert.All(slots, slot => Assert.True(slot.Empty));
  }

  private sealed class RecordingHand : IHandRelease {
    public bool? Kept;

    public void Release(bool keep) => Kept = keep;
  }

  /// <summary>Fails when the step's release ignores its keep key, so a kept hand empties.</summary>
  [Fact]
  public void An_interact_step_releases_the_player_with_the_keep_of_its_body() {
    var kept = new RecordingHand();
    InteractStep.Finish(kept, JObject.Parse("{\"keep\": true}"));
    var spent = new RecordingHand();
    InteractStep.Finish(spent, JObject.Parse("{}"));

    Assert.True(kept.Kept);
    Assert.False(spent.Kept);
  }

  /// <summary>Fails when the window step releases the player without keeping the hand, so a taken stack is
  /// cleared from the hotbar.</summary>
  [Fact]
  public void A_window_step_releases_the_player_keeping_the_hand() {
    var hand = new RecordingHand();
    WindowStep.Finish(hand);

    Assert.True(hand.Kept);
  }

  /// <summary>Fails when the click ignores the carried stack for a step with no <c>with</c>, or prefers it
  /// to the step's own.</summary>
  [Fact]
  public void An_interact_step_clicks_with_its_own_stack_else_the_carried_one() {
    var carried = new ItemStack(new Item(), 1);
    var own = new ItemStack(new Item(), 2);
    var slot = new DummySlot(carried);

    Assert.Same(own, InteractStep.ClickStack(own, slot));
    Assert.Same(carried, InteractStep.ClickStack(null, slot));
  }

  /// <summary>Fails when a kept hand is handed back to the pack, or an unkept one is not.</summary>
  [Fact]
  public void An_interact_step_returns_the_hand_to_the_pack_only_when_it_does_not_keep() {
    var stack = new ItemStack(new Item(), 2);
    var slot = new DummySlot(stack);

    Assert.Null(InteractStep.Returned(slot, keep: true));
    Assert.Same(stack, InteractStep.Returned(slot, keep: false));
  }

  /// <summary>Fails when the live slot read is off by one from the index <c>select</c> takes.</summary>
  [Fact]
  public void An_expect_of_a_slot_reads_the_hotbar_at_the_index_given() {
    var inventory = Substitute.For<IInventory>();
    var at2 = new ItemStack(new Item(), 2);
    var at3 = new ItemStack(new Item(), 3);
    inventory[2].Returns(new DummySlot(at2));
    inventory[3].Returns(new DummySlot(at3));

    Assert.Same(at2, ExpectStep.SlotStack(inventory, 2));
    Assert.Same(at3, ExpectStep.SlotStack(inventory, 3));
  }

  /// <summary>Fails when a name the player does not hold passes (S30).</summary>
  [Fact]
  public void A_key_the_player_does_not_hold_fails_the_step() {
    Assert.Equal(
      new[] { "sneak", "sprint" },
      InteractStep.KeyNames(JArray.Parse("[\"sneak\",\"sprint\"]"))
    );
    var e = Assert.Throws<StepFailure>(() =>
      InteractStep.KeyNames(JArray.Parse("[\"alt\"]"))
    );
    Assert.Equal("\"alt\" is not a key the scenario player holds.", e.Message);
  }

  /// <summary>Fails when an interaction runs without the scenario player (S31).</summary>
  [Fact]
  public void An_interact_without_the_scenario_player_fails_the_step() {
    var fixture = new LiveFixture();
    var steps = LiveFixture
      .Case("""[{ interact: { at: [0,0,0] } }]""")
      .Model.Steps;
    var e = Assert.Throws<StepFailure>(() => fixture.Run(steps));
    Assert.Equal("\"interact\" needs the scenario player.", e.Message);
  }

  /// <summary>Fails when a range runs short of its last slot (S35), a backward range passes (S36), or an
  /// index reads as the next slot (S37).</summary>
  [Fact]
  public void A_window_slot_is_an_index_or_its_inclusive_range() {
    Assert.Equal(new[] { 3 }, WindowStep.Slots(JToken.Parse("3")));
    Assert.Equal(new[] { 2, 3, 4 }, WindowStep.Slots(JToken.Parse("[2,4]")));
    var e = Assert.Throws<StepFailure>(() =>
      WindowStep.Slots(JToken.Parse("[4,2]"))
    );
    Assert.Equal("the slot range [4, 2] runs backward.", e.Message);
  }

  /// <summary>Fails when a value past 255 is sent as a byte (S38).</summary>
  [Fact]
  public void Press_data_is_bytes_from_0_to_255() {
    Assert.Equal(
      new byte[] { 0, 255 },
      WindowStep.Data(JArray.Parse("[0,255]"))
    );
    Assert.Throws<StepFailure>(() => WindowStep.Data(JArray.Parse("[256]")));
  }

  /// <summary>Fails when the open, close or a slot packet is taken for a button (S39).</summary>
  [Fact]
  public void A_button_packet_is_above_the_close_packet() {
    Assert.Equal(1002, WindowStep.ButtonId(1002));
    var e = Assert.Throws<StepFailure>(() => WindowStep.ButtonId(1001));
    Assert.Equal("press 1001: a button's packet id is above 1001.", e.Message);
  }

  /// <summary>Fails when a window runs without the scenario player (S40).</summary>
  [Fact]
  public void A_window_without_the_scenario_player_fails_the_step() {
    var fixture = new LiveFixture();
    var steps = LiveFixture
      .Case("""[{ window: { at: [0,0,0], open: true } }]""")
      .Model.Steps;
    var e = Assert.Throws<StepFailure>(() => fixture.Run(steps));
    Assert.Equal("\"window\" needs the scenario player.", e.Message);
  }

  /// <summary>Fails when a lit answer reads as waiting (S32), a prevent-default refusal reads as
  /// waiting (S33), or a hold at its own length keeps waiting (S34).</summary>
  [Fact]
  public void An_ignition_answer_reads_lit_refused_timed_out_or_waiting() {
    Assert.Equal(
      IgniteStep.IgniteVerdict.Lit,
      IgniteStep.Verdict(EnumIgniteState.IgniteNow, 0.1, 5)
    );
    Assert.Equal(
      IgniteStep.IgniteVerdict.Wait,
      IgniteStep.Verdict(EnumIgniteState.Ignitable, 3.9, 5)
    );
    Assert.Equal(
      IgniteStep.IgniteVerdict.TimedOut,
      IgniteStep.Verdict(EnumIgniteState.Ignitable, 5, 5)
    );
    Assert.Equal(
      IgniteStep.IgniteVerdict.Refused,
      IgniteStep.Verdict(EnumIgniteState.NotIgnitable, 0, 5)
    );
    Assert.Equal(
      IgniteStep.IgniteVerdict.Refused,
      IgniteStep.Verdict(EnumIgniteState.NotIgnitablePreventDefault, 0, 5)
    );
  }

  /// <summary>Fails when an ignition runs without the scenario player (S41).</summary>
  [Fact]
  public void An_ignite_without_the_scenario_player_fails_the_step() {
    var fixture = new LiveFixture();
    var steps = LiveFixture
      .Case("""[{ ignite: { at: [0,0,0] } }]""")
      .Model.Steps;
    var e = Assert.Throws<StepFailure>(() => fixture.Run(steps));
    Assert.Equal("\"ignite\" needs the scenario player.", e.Message);
  }
}
