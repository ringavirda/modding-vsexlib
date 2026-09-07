using System.Collections.Generic;
using ExpandedLib.Migrations;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// N2's completion marker: <see cref="ChunkColumnSweeperModSystem.Version"/> null (the default) sweeps
/// a column every call, exactly as before it was added; a non-null value marks the column after its
/// first sweep and skips it on every later call at that same value, and bumping the value re-sweeps.
/// </summary>
public class ChunkSweeperVersionTests {
  private sealed class VersionedSweeper : ChunkColumnSweeperModSystem {
    public string? VersionValue;
    public int ScanCalls;

    protected override string? Version => VersionValue;

    protected override bool BuildWork() => true;

    protected override int VisitCell(
      IBlockAccessor ba,
      BlockPos pos,
      int blockId
    ) => 0;

    protected override int VisitChunkEntities(IWorldChunk chunk) {
      ScanCalls++;
      return 0;
    }
  }

  private static Mod FakeMod() {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = "testmod" }
    );
    return mod;
  }

  // A chunk section with no blocks (so ScanChunk's cell loop is a no-op) whose moddata round-trips
  // through an in-memory dictionary, standing in for the persisted store the marker reads and writes.
  private static IWorldChunk FakeChunk() {
    var chunk = Substitute.For<IWorldChunk>();
    var data = Substitute.For<IChunkBlocks>();
    data.Length.Returns(0);
    chunk.Data.Returns(data);

    var store = new Dictionary<string, bool>();
    chunk
      .When(c => c.SetModdata<bool>(Arg.Any<string>(), Arg.Any<bool>()))
      .Do(ci => store[ci.ArgAt<string>(0)] = ci.ArgAt<bool>(1));
    chunk
      .GetModdata<bool>(Arg.Any<string>(), Arg.Any<bool>())
      .Returns(ci =>
        store.TryGetValue(ci.ArgAt<string>(0), out bool v)
          ? v
          : ci.ArgAt<bool>(1)
      );
    return chunk;
  }

  private static VersionedSweeper MakeSweeper(TestWorld world, string? version) {
    var sys = new VersionedSweeper { VersionValue = version };
    ReflectionHelpers.SetField(sys, "_sapi", world.Api);
    ReflectionHelpers.SetProperty(sys, "Mod", FakeMod());
    return sys;
  }

  [Fact]
  public void No_version_sweeps_the_column_every_call() {
    var world = new TestWorld();
    VersionedSweeper sys = MakeSweeper(world, version: null);
    IWorldChunk[] chunks = [FakeChunk()];

    ReflectionHelpers.Invoke(sys, "SweepColumn", 0, 0, chunks);
    ReflectionHelpers.Invoke(sys, "SweepColumn", 0, 0, chunks);

    Assert.Equal(2, sys.ScanCalls);
  }

  [Fact]
  public void A_version_marks_the_column_and_skips_it_next_call() {
    var world = new TestWorld();
    VersionedSweeper sys = MakeSweeper(world, "v1");
    IWorldChunk[] chunks = [FakeChunk()];

    ReflectionHelpers.Invoke(sys, "SweepColumn", 0, 0, chunks);
    ReflectionHelpers.Invoke(sys, "SweepColumn", 0, 0, chunks);

    Assert.Equal(1, sys.ScanCalls);
  }

  [Fact]
  public void Bumping_the_version_re_sweeps_an_already_marked_column() {
    var world = new TestWorld();
    VersionedSweeper sys = MakeSweeper(world, "v1");
    IWorldChunk[] chunks = [FakeChunk()];
    ReflectionHelpers.Invoke(sys, "SweepColumn", 0, 0, chunks);

    sys.VersionValue = "v2";
    ReflectionHelpers.Invoke(sys, "SweepColumn", 0, 0, chunks);

    Assert.Equal(2, sys.ScanCalls);
  }
}
