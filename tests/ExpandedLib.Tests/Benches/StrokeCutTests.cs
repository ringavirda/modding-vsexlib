using System;
using ExpandedLib.Industry.Benches;
using Xunit;

namespace ExpandedLib.Tests.Benches;

/// <summary>The cut of a stroke on the crank: its arc from the depth over the blade travel, its torque, and
/// where the arc sits in the stroke.</summary>
public class StrokeCutTests {
  // Fails when the ratio is dropped from the arc: the first row reads 1.31.
  [Theory]
  [InlineData(1f, 2.7f, 2f, 2.62f)]
  [InlineData(0.25f, 1.26f, 1f, 0.92f)]
  [InlineData(3f, 2.7f, 1f, MathF.PI)]
  [InlineData(3f, 2.7f, 2f, 2f * MathF.PI)]
  public void Cut_arc_follows_the_crank(
    float depth,
    float travel,
    float ratio,
    float arc
  ) => Assert.Equal(arc, StrokeCut.Arc(depth, travel, ratio), 2);

  [Fact]
  public void Torque_is_the_energy_over_the_arc() {
    Assert.Equal(500f, StrokeCut.Torque(1000f, 2f), 3);
    Assert.Equal(0f, StrokeCut.Torque(1000f, 0f));
  }

  // Fails when the arc is placed at the start of the stroke instead of ending at half of it.
  [Fact]
  public void The_cut_ends_at_half_the_stroke() {
    float stroke = 4f * MathF.PI;
    float arc = 1f;

    Assert.True(StrokeCut.InCut(stroke / 2f, stroke, arc));
    Assert.True(StrokeCut.InCut(stroke / 2f - arc, stroke, arc));
    Assert.False(StrokeCut.InCut(stroke / 2f + 0.01f, stroke, arc));
    Assert.False(StrokeCut.InCut(0.5f, stroke, arc));
  }

  // Fails when the share is a point test on the tick's angle: the first row reads 1 or 0.
  // The arc of a 6 rad stroke spans 2.08 to 3.0.
  [Theory]
  [InlineData(2f, 1f, 0.92f)]
  [InlineData(2.5f, 1f, 0.5f)]
  [InlineData(0f, 1f, 0f)]
  [InlineData(5.5f, 1f, 0f)]
  public void Cut_share_counts_the_part_of_the_tick_in_the_arc(
    float start,
    float advance,
    float share
  ) => Assert.Equal(share, StrokeCut.CutShare(start, advance, 6f, 0.92f), 2);
}
