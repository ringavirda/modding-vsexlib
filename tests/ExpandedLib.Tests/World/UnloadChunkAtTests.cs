using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.UnloadChunkAt"/> with its block entities unloaded, and <see cref="TestWorld.LoadChunkAt"/> bringing them back.</summary>
public class UnloadChunkAtTests {
  private sealed class Counter : BlockEntity {
    public int Value;
    public int Unloaded;
    public bool Initialised;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      Initialised = true;
    }

    public override void OnBlockUnloaded() {
      base.OnBlockUnloaded();
      Unloaded++;
    }

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetInt("value", Value);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldForResolving
    ) {
      base.FromTreeAttributes(tree, worldForResolving);
      Value = tree.GetInt("value");
    }
  }

  // Fails when the unload hook call is removed.
  [Fact]
  public void UnloadChunkAt_with_entities_runs_the_unload_hooks() {
    var w = new TestWorld();
    var pos = new BlockPos(3, 5, 3);
    var old = new Counter { Value = 7 };
    w.Place(
      pos,
      TestBlocks.Configure(new Block(), "test:unloadcounter", 92),
      old
    );
    w.Initialize(old);

    w.UnloadChunkAt(pos, unloadEntities: true);
    Assert.Equal(1, old.Unloaded);
    Assert.Null(w.GetBlockEntity(pos));

    w.LoadChunkAt(pos);
    var fresh = Assert.IsType<Counter>(w.GetBlockEntity(pos));
    Assert.NotSame(old, fresh);
    Assert.Equal(7, fresh.Value);
    Assert.True(fresh.Initialised);
    Assert.Equal(1, old.Unloaded);
  }
}
