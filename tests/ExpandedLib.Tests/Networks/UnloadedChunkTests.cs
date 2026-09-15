using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// What the graph walk does when it cannot see a cell: <c>GetBlock</c> answers the air block for an
/// unloaded chunk, not null.
/// </summary>
public class UnloadedChunkTests {
  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    return w;
  }

  /// <summary>A cell on the runs below, laid along +X across the chunk boundary at
  /// <c>GlobalConstants.ChunkSize</c>: offsets -2 and -1 sit in one chunk, 0 and 1 in the next.</summary>
  private static BlockPos Cell(int offsetFromBoundary) =>
    new(GlobalConstants.ChunkSize + offsetFromBoundary, 0, 0);

  /// <summary>Lays a four-cell east-west run straddling the chunk boundary.</summary>
  private static TestWorld BuildStraddlingRun() {
    var w = NewWorld();
    foreach (int offset in new[] { -2, -1, 0, 1 })
      w.PlaceNode(Cell(offset), "test", "we");

    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(4, w.NetworkAt(Cell(-1))!.Nodes.Count);
    return w;
  }

  /// <summary>Unloads the chunk holding the far half of the run.</summary>
  private static void UnloadFarHalf(TestWorld w) {
    w.UnloadChunkAt(Cell(0));

    Assert.True(w.IsChunkLoaded(Cell(-2)));
    Assert.True(w.IsChunkLoaded(Cell(-1)));
    Assert.False(w.IsChunkLoaded(Cell(0)));
    Assert.False(w.IsChunkLoaded(Cell(1)));
  }

  #region The walk cannot see an unloaded cell

  [Fact]
  public void An_unloaded_cell_answers_for_nothing_even_though_its_block_is_placed() {
    // A chunk unload hides the block as well as the block entity.
    var w = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    w.PlaceNode(pos, "test", "ns");
    Assert.NotNull(NetworkMembership.Resolve(w.Accessor, pos, "test"));

    w.UnloadChunkAt(pos);

    Assert.Null(NetworkMembership.Resolve(w.Accessor, pos, "test"));
    Assert.Equal(0, w.Accessor.GetBlock(pos).BlockId);
    Assert.Null(w.Accessor.GetBlockEntity(pos));
    Assert.Null(w.Accessor.GetChunkAtBlockPos(pos));
    // The store still holds the block.
    Assert.NotEqual(0, w.GetBlock(pos).BlockId);
  }

  #endregion

  #region A gap the walk cannot see is not a fracture

  [Fact]
  public void An_unloaded_chunk_does_not_split_the_network() {
    var w = BuildStraddlingRun();
    UnloadFarHalf(w);

    w.RemoveNode(Cell(-2));

    // The far cells are unreachable, not absent: they must not be dropped from the run.
    Assert.Single(w.Networks.AllNetworks);
    BlockNetwork? net = w.NetworkAt(Cell(-1));
    Assert.NotNull(net);
    Assert.Equal(3, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(Cell(1)));
  }

  [Fact]
  public void A_run_left_with_no_readable_node_at_all_is_left_whole() {
    // The gap can swallow the whole run, not just part of it.
    var w = BuildStraddlingRun();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-2));

    w.RemoveNode(Cell(-1));

    Assert.Single(w.Networks.AllNetworks);
    BlockNetwork? net = w.NetworkAt(Cell(0));
    Assert.NotNull(net);
    Assert.Equal(2, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(Cell(1)));
  }

  [Fact]
  public void A_genuinely_broken_run_still_fractures_while_every_chunk_is_loaded() {
    // Same removal, nothing unloaded: the run must still fracture.
    var w = BuildStraddlingRun();
    Assert.True(w.IsChunkLoaded(Cell(0)));

    w.RemoveNode(Cell(-1));

    Assert.Equal(2, w.Networks.AllNetworks.Count());
    Assert.NotSame(w.NetworkAt(Cell(-2)), w.NetworkAt(Cell(0)));
  }

  #endregion

  #region The chunk comes back

  [Fact]
  public void The_run_is_whole_again_when_the_chunk_comes_back() {
    var w = BuildStraddlingRun();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-2));

    w.LoadChunkAt(Cell(0));
    w.Tick();

    Assert.Single(w.Networks.AllNetworks);
    BlockNetwork? net = w.NetworkAt(Cell(-1));
    Assert.Equal(3, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(Cell(1)));
  }

  [Fact]
  public void A_resolved_run_fractures_again_at_once() {
    // Once the run is seen whole, the next break is decided on the spot.
    var w = BuildStraddlingRun();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-2));
    w.LoadChunkAt(Cell(0));
    w.Tick();
    Assert.Single(w.Networks.AllNetworks);

    w.RemoveNode(Cell(0));

    Assert.Equal(2, w.Networks.AllNetworks.Count());
    Assert.NotSame(w.NetworkAt(Cell(-1)), w.NetworkAt(Cell(1)));
  }

  [Fact]
  public void A_break_hidden_by_an_unloaded_chunk_splits_once_it_comes_back() {
    // The removal cuts the run in two; the walk could not prove it while half was unloaded.
    var w = BuildStraddlingRun();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-1));
    Assert.Single(w.Networks.AllNetworks);

    w.LoadChunkAt(Cell(0));
    w.Tick();

    Assert.Equal(2, w.Networks.AllNetworks.Count());
    Assert.Single(w.NetworkAt(Cell(-2))!.Nodes);
    Assert.Equal(2, w.NetworkAt(Cell(0))!.Nodes.Count);
    Assert.NotSame(w.NetworkAt(Cell(-2)), w.NetworkAt(Cell(0)));
  }

  [Fact]
  public void The_same_chunk_returning_twice_changes_nothing() {
    // The resume runs off the server tick and repeats every second; it must stay idempotent.
    var w = BuildStraddlingRun();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-2));
    w.LoadChunkAt(Cell(0));
    w.Tick();
    BlockNetwork? afterFirst = w.NetworkAt(Cell(1));

    w.LoadChunkAt(Cell(0));
    w.Tick(3);

    Assert.Single(w.Networks.AllNetworks);
    Assert.Same(afterFirst, w.NetworkAt(Cell(1)));
    Assert.Equal(3, afterFirst!.Nodes.Count);
  }

  [Fact]
  public void One_of_two_missing_chunks_coming_back_is_not_enough() {
    // Two gaps at once: the middle returning is not enough while the far chunk stays unaccounted for.
    const int cs = GlobalConstants.ChunkSize;
    var w = NewWorld();
    for (int x = cs - 2; x <= 2 * cs; x++)
      w.PlaceNode(new BlockPos(x, 0, 0), "test", "we");
    var stub = new BlockPos(cs - 2, 0, 0);
    var cut = new BlockPos(cs - 1, 0, 0);
    var middle = new BlockPos(cs, 0, 0);
    var far = new BlockPos(2 * cs, 0, 0);
    int laid = cs + 3;
    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(laid, w.NetworkAt(stub)!.Nodes.Count);

    w.UnloadChunkAt(middle);
    w.UnloadChunkAt(far);
    Assert.True(w.IsChunkLoaded(cut));
    w.RemoveNode(cut);

    w.LoadChunkAt(middle);
    w.Tick();

    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(laid - 1, w.NetworkAt(stub)!.Nodes.Count);

    w.LoadChunkAt(far);
    w.Tick();

    Assert.Equal(2, w.Networks.AllNetworks.Count());
    Assert.Single(w.NetworkAt(stub)!.Nodes);
    Assert.Equal(laid - 2, w.NetworkAt(far)!.Nodes.Count);
  }

  #endregion

  #region A footprint cell bridging the run

  /// <summary>Lays the same straddling run with a mega-block footprint cell in place of the first cell
  /// across the boundary.</summary>
  private static TestWorld BuildRunBridgedByFiller() {
    var w = NewWorld();
    w.PlaceNode(Cell(-2), "test", "we");
    w.PlaceNode(Cell(-1), "test", "we");
    w.PlaceFillerNode(Cell(0), "test", "we");
    w.PlaceNode(Cell(1), "test", "we");

    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(4, w.NetworkAt(Cell(0))!.Nodes.Count);
    // The bridging block is not a network block; only its membership carries the run.
    Assert.IsNotAssignableFrom<BlockNetworkNode>(w.GetBlock(Cell(0)));
    return w;
  }

  [Fact]
  public void A_run_bridged_through_a_footprint_cell_survives_that_cells_chunk_unloading() {
    // A footprint cell answers for nothing while its chunk is unloaded.
    var w = BuildRunBridgedByFiller();
    UnloadFarHalf(w);

    w.RemoveNode(Cell(-2));

    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(3, w.NetworkAt(Cell(1))!.Nodes.Count);
    Assert.Same(w.NetworkAt(Cell(-1)), w.NetworkAt(Cell(1)));
  }

  [Fact]
  public void The_bridged_run_is_one_network_again_when_the_footprint_cell_returns() {
    var w = BuildRunBridgedByFiller();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-2));

    w.LoadChunkAt(Cell(0));
    w.Tick();

    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(3, w.NetworkAt(Cell(1))!.Nodes.Count);
  }

  [Fact]
  public void The_footprint_cell_rejoins_the_run_after_a_full_load_from_the_save_tree() {
    // Each block entity is rebuilt from its save tree and re-initialised; the next tick reconnects the
    // run.
    var w = BuildRunBridgedByFiller();
    UnloadFarHalf(w);
    w.RemoveNode(Cell(-2));

    w.LoadChunkAt(Cell(0));
    w.Reload(Cell(0));
    w.Reload(Cell(1));
    w.Tick();

    Assert.Single(w.Networks.AllNetworks);
    BlockNetwork? net = w.NetworkAt(Cell(1));
    Assert.Equal(3, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(Cell(-1)));
  }

  #endregion
}
