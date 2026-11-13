using System.Collections.Generic;
using ExpandedLib.Assembling;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The client's pure parts: the line shown, which placement is shown, and the hold that fixes it.</summary>
public class HandAssemblyClientTests {
  private static readonly CraftToolDefault Wrench = new(
    "test:wrench-*",
    "hammerhit",
    "sounds/effect/latch",
    Stroke: 1f,
    Strike: 0.3f
  );

  private sealed class Effects : ICraftEffects {
    public List<string> Log { get; } = [];

    public void StartAnimation(string animation) => Log.Add("start " + animation);

    public void StopAnimation(string animation) => Log.Add("stop " + animation);

    public void Strike(string sound) => Log.Add("strike " + sound);
  }

  private static string Echo(string key, object[] args) =>
    key + "[" + string.Join("|", args) + "]";

  #region The line

  // Fails when the joins, the open ends or the cost are left out of the line, or the joins lose their faces.
  [Fact]
  public void Hud_NamesJoinsOpenEndsAndCost() {
    string line = HandAssemblyHud.Line(
      Echo,
      3,
      4,
      "Tee",
      [("Pipe", BlockFacing.NORTH), ("Pipe", BlockFacing.SOUTH)],
      [BlockFacing.EAST],
      2,
      "Bolt bundle",
      37
    );

    Assert.Equal(
      "exlib:handassembly-hud-head[3|4|Tee] "
        + "exlib:handassembly-hud-joins[Pipe (N), Pipe (S)]; "
        + "exlib:handassembly-hud-open[E] | "
        + "exlib:handassembly-hud-cost[2|Bolt bundle|37]",
      line
    );
  }

  // Fails when a placement with no open end and no cost still carries an open or cost clause.
  [Fact]
  public void Hud_DropsOpenEndsAndCostWhenThereAreNone() {
    string line = HandAssemblyHud.Line(
      Echo,
      1,
      1,
      "Pipe",
      [],
      [],
      0,
      "Bolt bundle",
      0
    );

    Assert.Equal(
      "exlib:handassembly-hud-head[1|1|Pipe] exlib:handassembly-hud-nojoins[]",
      line
    );
  }

  #endregion

  #region The hold

  // Fails when the hold is not one stroke a joint, or the floor for none is not half a second.
  [Theory]
  [InlineData(2, 1.05f, 2.1f)]
  [InlineData(1, 0.8f, 0.8f)]
  [InlineData(0, 1.05f, 0.5f)]
  public void Hold_StrokePerJointHalfSecondForNone(
    int joints,
    float stroke,
    float expected
  ) => Assert.Equal(expected, HandAssemblyHold.Seconds(joints, stroke), 3);

  // Fails when the animation or the sound is not played once per stroke, or the animation is not stopped at the end.
  [Fact]
  public void Hold_PlaysAnimationAndSoundEachStrokeAndStopsAtTheEnd() {
    var hold = new HandAssemblyHold();
    var effects = new Effects();
    hold.Begin("c|n", "tee", 2, 2, Wrench);

    Assert.False(hold.Advance(0.1f, effects));
    Assert.Equal(["start hammerhit"], effects.Log);
    Assert.False(hold.Advance(0.3f, effects));
    Assert.Equal(
      ["start hammerhit", "strike sounds/effect/latch"],
      effects.Log
    );
    Assert.False(hold.Advance(1f, effects));
    Assert.Equal(
      [
        "start hammerhit",
        "strike sounds/effect/latch",
        "start hammerhit",
        "strike sounds/effect/latch",
      ],
      effects.Log
    );
    Assert.True(hold.Advance(0.7f, effects));
    Assert.Equal("stop hammerhit", effects.Log[^1]);
    Assert.False(hold.Active);
  }

  // Fails when progress is not the fraction of the time done.
  [Fact]
  public void Hold_ProgressIsTheFractionDone() {
    var hold = new HandAssemblyHold();
    hold.Begin("c|n", "tee", 2, 2, Wrench);

    hold.Advance(0.5f, null);

    Assert.Equal(0.25f, hold.Progress, 3);
  }

  // Fails when a hold ends early or never reports its end.
  [Fact]
  public void Hold_EndsAfterItsTime() {
    var hold = new HandAssemblyHold();
    hold.Begin("c|n", "tee", 2, 1, Wrench);

    Assert.False(hold.Advance(0.9f, null));
    Assert.True(hold.Active);
    Assert.True(hold.Advance(0.2f, null));
    Assert.False(hold.Active);
  }

  // Fails when a hold survives the button's release, a new target or a new slot, or ends when none of them happens.
  [Theory]
  [InlineData("same", true)]
  [InlineData("release", false)]
  [InlineData("target", false)]
  [InlineData("what", false)]
  [InlineData("slot", false)]
  public void Hold_CancelsOnReleaseNewTargetOrSlot(string change, bool active) {
    var hold = new HandAssemblyHold();
    var effects = new Effects();
    hold.Begin("c|n", "tee", 2, 2, Wrench);
    hold.Advance(0.1f, effects);

    bool kept = change switch {
      "release" => Release(hold, effects),
      "target" => hold.Keep("c2|n", "tee", 2, effects),
      "what" => hold.Keep("c|n", "cross", 2, effects),
      "slot" => hold.Keep("c|n", "tee", 3, effects),
      _ => hold.Keep("c|n", "tee", 2, effects),
    };

    Assert.Equal(active, kept);
    Assert.Equal(active, hold.Active);
    Assert.Equal(!active, effects.Log.Contains("stop hammerhit"));
  }

  private static bool Release(HandAssemblyHold hold, ICraftEffects effects) {
    hold.Release(effects);
    return hold.Active;
  }

  #endregion

  #region Stepping

  // Fails when a step past the last placement does not return to the first.
  [Fact]
  public void Step_WrapsAfterLast() {
    var stepper = new HandAssemblyStepper();
    stepper.Retarget("a", ["ns", "we", "ne"]);

    Assert.Equal("we", stepper.Step());
    Assert.Equal("ne", stepper.Step());
    Assert.Equal("ns", stepper.Step());
    Assert.Equal(0, stepper.Index);
  }

  // Fails when a recompute of the same target resets the shown placement.
  [Fact]
  public void Step_KeepsShownCandidateWhileOffered() {
    var stepper = new HandAssemblyStepper();
    stepper.Retarget("a", ["ns", "we", "ne"]);
    stepper.Step();

    stepper.Retarget("a", ["ne", "we", "ns"]);

    Assert.Equal("we", stepper.Shown);
    Assert.Equal(1, stepper.Index);
  }

  // Fails when the shown placement is kept after it is no longer offered.
  [Fact]
  public void Step_ReturnsToFirstWhenShownIsNoLongerOffered() {
    var stepper = new HandAssemblyStepper();
    stepper.Retarget("a", ["ns", "we", "ne"]);
    stepper.Step();

    stepper.Retarget("a", ["ns", "ne"]);

    Assert.Equal("ns", stepper.Shown);
  }

  // Fails when a new target keeps the index of the old one.
  [Fact]
  public void Step_NewTargetStartsAtFirst() {
    var stepper = new HandAssemblyStepper();
    stepper.Retarget("a", ["ns", "we", "ne"]);
    stepper.Step();
    stepper.Step();

    stepper.Retarget("b", ["ns", "we", "ne"]);

    Assert.Equal("ns", stepper.Shown);
  }

  // Fails when a step with nothing offered returns a placement or throws.
  [Fact]
  public void Step_WithNothingOfferedShowsNothing() {
    var stepper = new HandAssemblyStepper();
    stepper.Retarget("a", []);

    Assert.Null(stepper.Step());
    Assert.Null(stepper.Shown);
  }

  #endregion
}
