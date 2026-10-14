using System.Linq;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
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
        new CuboidfWithId(new Cuboidf(0.25f, 0.5f, 0.25f, 0.75f, 1f, 0.5f)) {
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
      (false, true, false),
      (controls.Sneak, controls.CtrlKey, controls.Sprint)
    );
    ScenarioPlayer.Hold(controls, ["sprint", "sneak"]);
    Assert.Equal(
      (true, false, true),
      (controls.Sneak, controls.CtrlKey, controls.Sprint)
    );
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
