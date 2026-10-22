using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.FastForwardGroupOf"/> and <see cref="TestWorld.ReplayedSteps"/>, the harness's reads of a fast-forward group.</summary>
public class TestWorldFastForwardReadTests {
  private static readonly BlockPos A = new(0, 5, 0);
  private static readonly BlockPos B = new(4, 5, 0);
  private static readonly BlockPos Alone = new(0, 5, 20);

  // Fails when ReplayedSteps returns 0, or the group read drops its members.
  [Fact]
  public void The_harness_reads_a_group_and_its_replay_steps() {
    var w = new TestWorld();
    FastForwardMemberTests.Place(w, A, B);
    FastForwardMemberTests.Place(w, B);
    FastForwardMemberTests.Place(w, Alone);
    w.Tick();
    w.Networks.FastForward.MaxStepsPerCall = 3;
    w.AdvanceHours(1);

    w.Tick();

    Assert.Equal([A, B], w.FastForwardGroupOf(A).OrderBy(p => p.X));
    Assert.Equal(11, w.ReplayedSteps(A));
    Assert.Equal([Alone], w.FastForwardGroupOf(Alone));
    Assert.Empty(w.FastForwardGroupOf(new BlockPos(9, 9, 9)));
    Assert.Equal(0, w.ReplayedSteps(new BlockPos(9, 9, 9)));
  }
}
