using System.Linq;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The scenario player's held verbs: the hold, its keys, the window's packets and the ignition
/// verdicts (scenarios.md, The scenario player).</summary>
public class ScenarioPlayerVerbTests {
  /// <summary>Fails when a hold runs past its seconds (S27).</summary>
  [Fact]
  public void A_hold_goes_on_while_the_time_held_is_under_its_seconds() {
    Assert.True(InteractStep.StillHeld(0.9, 1));
    Assert.False(InteractStep.StillHeld(1.0, 1));
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

  /// <summary>Fails when an interaction runs without the scenario player (S39).</summary>
  [Fact]
  public void An_interact_without_the_scenario_player_fails_the_step() {
    var fixture = new LiveFixture();
    var steps = LiveFixture
      .Case("""[{ interact: { at: [0,0,0] } }]""")
      .Model.Steps;
    var e = Assert.Throws<StepFailure>(() => fixture.Run(steps));
    Assert.Equal("\"interact\" needs the scenario player.", e.Message);
  }
}
