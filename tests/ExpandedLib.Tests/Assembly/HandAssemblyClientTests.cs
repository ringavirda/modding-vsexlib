using System;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The client's pure parts: the line shown and which placement is shown.</summary>
public class HandAssemblyClientTests {
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

  // Fails when the ring leaves the flange rim, sits inside the cell or on the opposite face, or loses segments.
  [Theory]
  [InlineData("north")]
  [InlineData("east")]
  [InlineData("south")]
  [InlineData("west")]
  [InlineData("up")]
  [InlineData("down")]
  public void Marks_RingSpansTheFlangeRim(string face) {
    BlockFacing arm = BlockFacing.FromCode(face);
    MeshData m = HandAssemblyMarks.Ring(arm);
    Vec3i n = arm.Normali;
    float plane = n.X + n.Y + n.Z > 0 ? 1.005f : -0.005f;
    float min = float.MaxValue;
    float max = 0f;

    for (int i = 0; i < m.VerticesCount; i++) {
      float[] p = [m.xyz[3 * i], m.xyz[(3 * i) + 1], m.xyz[(3 * i) + 2]];
      int[] normal = [n.X, n.Y, n.Z];
      double radiusSquared = 0;
      for (int axis = 0; axis < 3; axis++) {
        if (normal[axis] != 0)
          Assert.Equal(plane, p[axis], 4);
        else
          radiusSquared += (p[axis] - 0.5) * (p[axis] - 0.5);
      }
      float radius = (float)Math.Sqrt(radiusSquared);
      min = Math.Min(min, radius);
      max = Math.Max(max, radius);
    }

    Assert.Equal(2.5f / 16f, min, 4);
    Assert.Equal(4f / 16f, max, 4);
    Assert.Equal(16 * 6, m.IndicesCount);
  }

  // Fails when the ring covers the bore.
  [Fact]
  public void Marks_BoreStaysClear() {
    foreach (BlockFacing arm in BlockFacing.ALLFACES) {
      MeshData m = HandAssemblyMarks.Ring(arm);
      Vec3i n = arm.Normali;
      float[] Radii(int i) {
        float[] p = [m.xyz[3 * i], m.xyz[(3 * i) + 1], m.xyz[(3 * i) + 2]];
        int[] normal = [n.X, n.Y, n.Z];
        double sum = 0;
        for (int axis = 0; axis < 3; axis++)
          if (normal[axis] == 0)
            sum += (p[axis] - 0.5) * (p[axis] - 0.5);
        return [p[0], p[1], p[2], (float)Math.Sqrt(sum)];
      }

      for (int i = 0; i < m.VerticesCount; i++)
        Assert.True(Radii(i)[3] >= (2.5f / 16f) - 1e-4f);

      for (int k = 0; k < HandAssemblyMarks.Segments; k++) {
        float[] a = Radii(2 * k);
        float[] b = Radii(2 * ((k + 1) % HandAssemblyMarks.Segments));
        float mx = (a[0] + b[0]) / 2f;
        float my = (a[1] + b[1]) / 2f;
        float mz = (a[2] + b[2]) / 2f;
        double sum = 0;
        float[] mid = [mx, my, mz];
        int[] normal = [n.X, n.Y, n.Z];
        for (int axis = 0; axis < 3; axis++)
          if (normal[axis] == 0)
            sum += (mid[axis] - 0.5) * (mid[axis] - 0.5);
        Assert.True(Math.Sqrt(sum) > 2f / 16f);
      }
    }
  }

  // Fails when a mark draws opaque.
  [Fact]
  public void Marks_AreSeeThrough() {
    Assert.Equal(0x60 / 255f, HandAssemblyMarks.Color(true).A, 4);
    Assert.Equal(0x60 / 255f, HandAssemblyMarks.Color(false).A, 4);
  }

  // Fails when the open and the joined colours are swapped.
  [Fact]
  public void Mark_OpenIsAmberJoinedIsGreen() {
    Vec4f open = HandAssemblyMarks.Color(true);
    Vec4f joined = HandAssemblyMarks.Color(false);

    Assert.True(open.R > open.G && open.G > open.B, "open is amber");
    Assert.True(joined.G > joined.R && joined.G > joined.B, "joined is green");
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
