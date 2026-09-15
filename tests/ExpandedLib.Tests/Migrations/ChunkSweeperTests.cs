using ExpandedLib.Migrations;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ChunkColumnSweeperModSystem"/>'s base-class contract: BuildWork runs once, and
/// ShouldVisit/VisitCell gate which cells a subclass's sweep touches.
/// </summary>
public class ChunkSweeperTests {
  private sealed class CountingSweeper : ChunkColumnSweeperModSystem {
    public int BuildWorkCalls;
    public bool HasWork = true;

    protected override bool BuildWork() {
      BuildWorkCalls++;
      return HasWork;
    }

    protected override int VisitCell(
      IBlockAccessor ba,
      BlockPos pos,
      int blockId
    ) => 0;
  }

  [Fact]
  public void EnsureInitialized_builds_the_work_table_exactly_once() {
    var sys = new CountingSweeper();

    bool first = (bool)ReflectionHelpers.Invoke(sys, "EnsureInitialized")!;
    bool second = (bool)ReflectionHelpers.Invoke(sys, "EnsureInitialized")!;

    Assert.True(first);
    Assert.True(second);
    Assert.Equal(1, sys.BuildWorkCalls);
  }

  [Fact]
  public void EnsureInitialized_reports_whatever_BuildWork_answered() {
    var sys = new CountingSweeper { HasWork = false };

    bool hasWork = (bool)ReflectionHelpers.Invoke(sys, "EnsureInitialized")!;

    Assert.False(hasWork);
    Assert.Equal(1, sys.BuildWorkCalls);
  }

  [Fact]
  public void ShouldVisit_admits_every_block_id_by_default() {
    var sys = new CountingSweeper();

    Assert.True((bool)ReflectionHelpers.Invoke(sys, "ShouldVisit", 12345)!);
  }
}
