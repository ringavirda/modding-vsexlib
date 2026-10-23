using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Node placements in two worlds at the same position, side by side:
/// <see cref="BlockNetworkNode"/> keeps each placement's orientation choices apart.</summary>
public class PlacementIsolationTests {
  private static readonly BlockPos At = new(5, 5, 5);

  private static readonly FieldInfo Store = typeof(BlockNetworkNode).GetField(
    "_tempOrientationsStore",
    BindingFlags.NonPublic | BindingFlags.Static
  )!;

  private sealed class IsoNetwork(BlockNetworkModSystem system)
    : BlockNetwork(system) {
    public override string NetworkType => "iso";

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) { }

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) { }
  }

  private sealed class StoreReader(Block block, Barrier barrier)
    : BlockBehavior(block) {
    public int Seen;

    public override void OnBlockPlaced(
      IWorldAccessor world,
      BlockPos blockPos,
      ref EnumHandling handling
    ) {
      barrier.SignalAndWait(TimeSpan.FromSeconds(30));
      Seen = Math.Max(Seen, ((ICollection)Store.GetValue(null)!).Count);
    }
  }

  private static bool Place(TestWorld w, Block block) {
    string code = "";
    return block.TryPlaceBlock(
      w.World,
      null!,
      new ItemStack(block),
      new BlockSelection { Position = At.Copy(), Face = BlockFacing.EAST },
      ref code
    );
  }

  // Fails when the store is keyed by position alone: the second placement overwrites the first's
  // entry and the readers see one entry.
  [Fact]
  public void Two_worlds_placing_at_one_position_hold_two_entries() {
    var barrier = new Barrier(2);
    var readers = new StoreReader[2];
    var worlds = new TestWorld[2];
    var blocks = new Block[2];
    for (int i = 0; i < 2; i++) {
      worlds[i] = new TestWorld();
      worlds[i].RegisterNetwork("iso", sys => new IsoNetwork(sys));
      TestNetworkBlock block = TestNetworkBlock.Family(
        "iso",
        "test:iso",
        "Axis",
        ["ns", "we", "ud"],
        900
      )[1];
      readers[i] = new StoreReader(block, barrier);
      block.BlockBehaviors = [.. block.BlockBehaviors, readers[i]];
      block.CollectibleBehaviors = [.. block.CollectibleBehaviors, readers[i]];
      worlds[i].Register(block);
      blocks[i] = block;
    }

    Task[] tasks =
    [
      .. new[] { 0, 1 }.Select(i =>
        Task.Factory.StartNew(
          () => Place(worlds[i], blocks[i]),
          CancellationToken.None,
          TaskCreationOptions.LongRunning,
          TaskScheduler.Default
        )
      ),
    ];
    Task.WaitAll(tasks);

    Assert.Equal(2, Math.Max(readers[0].Seen, readers[1].Seen));
  }
}
