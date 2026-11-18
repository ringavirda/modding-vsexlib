using System.Collections.Generic;
using ExpandedLib.Assembling;
using ExpandedLib.Catalogues;
using ExpandedLib.Helpers;
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

    public void StartAnimation(string animation) =>
      Log.Add("start " + animation);

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
      ["start hammerhit", "strike " + HandAssemblyHold.StrokeSound],
      effects.Log
    );
    Assert.False(hold.Advance(1f, effects));
    Assert.Equal(
      [
        "start hammerhit",
        "strike " + HandAssemblyHold.StrokeSound,
        "start hammerhit",
        "strike " + HandAssemblyHold.StrokeSound,
      ],
      effects.Log
    );
    Assert.True(hold.Advance(0.7f, effects));
    Assert.Equal("stop hammerhit", effects.Log[^1]);
    Assert.False(hold.Active);
  }

  // Fails when a hold with no tool strikes no sound.
  [Fact]
  public void Hold_WithoutAToolStrikesTheSound() {
    var hold = new HandAssemblyHold();
    var effects = new Effects();
    hold.Begin("c|n", "tee", 2, 1, null);

    hold.Advance(0.4f, effects);

    Assert.Equal(["strike " + HandAssemblyHold.StrokeSound], effects.Log);
  }

  // Fails when the wrench plays its latch instead of the hand assembly sound.
  [Fact]
  public void Hold_WithTheWrenchStrikesTheHandAssemblySound() {
    var hold = new HandAssemblyHold();
    var effects = new Effects();
    hold.Begin("c|n", "tee", 2, 1, Wrench);

    hold.Advance(0.4f, effects);

    Assert.Equal(
      ["start hammerhit", "strike game:sounds/block/heavymetal-hit"],
      effects.Log
    );
  }

  // Fails when a tool with a sound of its own has it replaced by the hand assembly sound.
  [Fact]
  public void Hold_WithAHammerKeepsItsOwnHit() {
    var hold = new HandAssemblyHold();
    var effects = new Effects();
    hold.Begin("c|n", "tee", 2, 1, CraftToolDefault.Hammer);

    hold.Advance(0.5f, effects);

    Assert.Equal(
      ["start hammerhit", "strike sounds/effect/anvilhit"],
      effects.Log
    );
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

  #region The projection colour

  // Fails when the ghost is tinted a fixed colour instead of the block's, or keeps another alpha.
  [Fact]
  public void Projection_GhostTakesTheBlocksOwnColourAt0x60() {
    Assert.Equal(
      ColorUtil.ColorFromRgba(10, 20, 30, 0x60),
      HandAssemblyClient.GhostColor(
        ColorUtil.ColorFromRgba(10, 20, 30, 255),
        false
      )
    );
  }

  // Fails when the tint reads the colour bytes in another order than the highlight does.
  [Fact]
  public void Projection_TintReadsTheHighlightByteOrder() {
    Vec4f t = ProjectionColors.Tint(ColorUtil.ColorFromRgba(10, 20, 30, 0x60));

    Assert.Equal(10 / 255f, t.R, 4);
    Assert.Equal(20 / 255f, t.G, 4);
    Assert.Equal(30 / 255f, t.B, 4);
    Assert.Equal(0x60 / 255f, t.A, 4);
  }

  // Fails when a short placement keeps the block's colour instead of the refused red.
  [Fact]
  public void Projection_ShortGhostIsRefused() {
    int block = ColorUtil.ColorFromRgba(71, 68, 69, 255);

    Assert.Equal(
      ProjectionColors.Refused,
      HandAssemblyClient.GhostColor(block, true)
    );
    Assert.NotEqual(
      ProjectionColors.Refused,
      HandAssemblyClient.GhostColor(block, false)
    );
  }

  #endregion

  #region The marks

  // Fails when a mark stands inside the cell or on the opposite face (a sign flipped on the face normal), leaves the face's centre, or is not 6/16 by 1/16.
  [Theory]
  [InlineData("north")]
  [InlineData("east")]
  [InlineData("south")]
  [InlineData("west")]
  [InlineData("up")]
  [InlineData("down")]
  public void Mark_IsAPlateCentredJustOutsideTheArmFace(string face) {
    BlockFacing arm = BlockFacing.FromCode(face);
    Cuboidf box = HandAssemblyMarks.Box(arm);
    float[] lo = [box.X1, box.Y1, box.Z1];
    float[] hi = [box.X2, box.Y2, box.Z2];
    int[] normal = [arm.Normali.X, arm.Normali.Y, arm.Normali.Z];

    for (int axis = 0; axis < 3; axis++) {
      if (normal[axis] > 0) {
        Assert.InRange(lo[axis], 1f, 1.02f);
        Assert.Equal(1f / 16f, hi[axis] - lo[axis], 4);
      } else if (normal[axis] < 0) {
        Assert.InRange(hi[axis], -0.02f, 0f);
        Assert.Equal(1f / 16f, hi[axis] - lo[axis], 4);
      } else {
        Assert.Equal(0.5f, (lo[axis] + hi[axis]) / 2f, 4);
        Assert.Equal(6f / 16f, hi[axis] - lo[axis], 4);
      }
    }
  }

  // Fails when the open and the joined colours are swapped.
  [Fact]
  public void Mark_OpenIsAmberJoinedIsGreen() {
    Vec4f open = HandAssemblyMarks.Color(true);
    Vec4f joined = HandAssemblyMarks.Color(false);

    Assert.True(open.R > open.G && open.G > open.B, "open is amber");
    Assert.True(joined.G > joined.R && joined.G > joined.B, "joined is green");
    Assert.Equal(1f, open.A);
    Assert.Equal(1f, joined.A);
  }

  #endregion

  #region The line's place

  // Fails when the line sits inside the held-item help, as at an offset of -140.
  [Theory]
  [InlineData(2)]
  [InlineData(3)]
  public void HudLine_ClearsTheHeldHelp(int helpLines) {
    double helpTop = 95 + 80 + ((helpLines - 1) * (25 + 8));

    Assert.True(
      -HandAssemblyHudDialog.BottomOffset(HandAssemblyHudDialog.HeldHelpLines)
        > helpTop
    );
    Assert.True(-HandAssemblyHudDialog.BottomOffset(helpLines) > helpTop);
  }

  #endregion
}
