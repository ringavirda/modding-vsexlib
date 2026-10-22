using System;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A pipe cell carrying <see cref="BEBehaviorPipeFlowCell"/> joins the physical pipe run: the run builds
/// its node, edges and openings, drops them with the cell, and the cell saves its node.
/// </summary>
public class FlowCellTests {
  private const double Atm = 101325;
  private const double AirConstant = 287;
  private const float Tiny = 1e-12f;

  private static readonly FlowBore CastMain = new(
    0.25,
    5,
    FlowBore.BurstFor(5),
    4,
    0.03,
    BlockPipe.FlangedJoint,
    90,
    0.015
  );

  #region Fixtures

  private sealed class CastMainCell(BlockEntity be) : BEBehaviorPipeFlowCell(be) {
    public override FlowBore? Bore => CastMain;
  }

  private static readonly FlowBore WeakMain = new(
    0.2,
    3,
    FlowBore.BurstFor(3),
    2,
    0.05,
    BlockPipe.FlangedJoint
  );

  private sealed class WeakCell(BlockEntity be) : BEBehaviorPipeFlowCell(be) {
    public override FlowBore? Bore => WeakMain;
  }

  private sealed class WeakCellEntity : BlockEntityNetworkNode {
    public override string NetworkType {
      get => "pipeflow";
      set { }
    }

    public WeakCellEntity() => Behaviors.Add(new WeakCell(this));
  }

  private sealed class FlowCellEntity : BlockEntityNetworkNode {
    public override string NetworkType {
      get => "pipeflow";
      set { }
    }

    public FlowCellEntity() => Behaviors.Add(new CastMainCell(this));
  }

  private sealed class LumpedCellEntity : BlockEntityNetworkNode {
    public override string NetworkType {
      get => "pipe";
      set { }
    }

    public LumpedCellEntity() => Behaviors.Add(new CastMainCell(this));
  }

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("pipeflow", sys => new PipeFlowNetwork(sys));
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    return w;
  }

  private static BlockPos Cell(int x) => new(x, 0, 0);

  private static NodeKey Key(int x) => NodeKey.Centre(Cell(x));

  private static PipeFlowNetwork RunAt(TestWorld w, int x) =>
    (PipeFlowNetwork)w.NetworkAt(Cell(x))!;

  private static BEBehaviorPipeFlowCell CellOf(TestWorld w, int x) =>
    w.GetBlockEntity(Cell(x))!.GetBehavior<BEBehaviorPipeFlowCell>();

  private static double VolumeM3 => VolumeOf(CastMain);

  private static double VolumeOf(FlowBore bore) =>
    Math.PI / 4 * bore.BoreMetres * bore.BoreMetres;

  /// <summary>A save entry for one cell of <paramref name="bore"/> holding air at <paramref name="atm"/> absolute and 20 C.</summary>
  private static TreeAttribute Entry(double atm, FlowBore? bore = null) {
    var tree = new TreeAttribute();
    PipeFlowSave.Write(
      tree,
      0,
      new PipeNodeSave(
        atm * Atm * VolumeOf(bore ?? CastMain) / (AirConstant * 293.15),
        0,
        0,
        20,
        20,
        0,
        0
      )
    );
    return tree;
  }

  private static void Lay(
    TestWorld w,
    int x,
    TreeAttribute? tree = null,
    BlockEntity? be = null
  ) {
    be ??= new FlowCellEntity();
    w.Place(Cell(x), TestNetworkBlock.Create("pipeflow", "we", 1), be);
    if (tree != null)
      be.GetBehavior<BEBehaviorPipeFlowCell>()
        .FromTreeAttributes(tree, w.World);
    w.Initialize(be);
  }

  private static void Tick(TestWorld w, int x, float dt = Tiny) =>
    RunAt(w, x).OnTick(w.Accessor, dt, w.Networks);

  private static void RemoveCell(TestWorld w, int x) {
    w.Accessor.RemoveBlockEntity(Cell(x));
    w.Place(Cell(x), w.Air);
  }

  private static int FlowingOpenings(PipeFlowStore store) {
    int count = 0;
    for (int o = 0; o < store.OpeningCount; o++)
      if (Math.Abs(store.OpeningFlowKgPerSecond(o)) > 1e-6)
        count++;
    return count;
  }

  #endregion

  // Fails when edges are laid only between cells built in the same reconcile, or when the node is built without its wall.
  [Fact]
  public void Cell_BuildsAmbientNodeAndEdges() {
    var w = NewWorld();
    Lay(w, 0);
    Lay(w, 2);
    Tick(w, 0);
    Tick(w, 2);
    Lay(w, 1);
    var run = RunAt(w, 1);
    Assert.Equal(3, run.Nodes.Count);

    Tick(w, 1);

    var store = run.Store;
    Assert.Equal(3, store.NodeCount);
    Assert.Equal(2, store.EdgeCount);
    for (int x = 0; x < 3; x++) {
      int node = store.IndexOf(Key(x));
      Assert.Equal(VolumeM3, store.VolumeM3(node), 6);
      Assert.InRange(store.PressurePa(node) / Atm, 0.999, 1.001);
      Assert.Equal(41400, store.WallCapacityJPerK(node), 6);
      Assert.Equal(20, store.TemperatureC(node), 6);
    }
    Assert.True(
      store.EdgeBetween(store.IndexOf(Key(0)), store.IndexOf(Key(1))) >= 0
    );
    Assert.True(
      store.EdgeBetween(store.IndexOf(Key(1)), store.IndexOf(Key(2))) >= 0
    );
  }

  // Fails when a face toward a joined neighbour also opens.
  [Fact]
  public void OpenEnd_VentsAtItsBore() {
    var w = NewWorld();
    Lay(w, 10, Entry(3));
    Lay(w, 11, Entry(3));
    var run = RunAt(w, 10);

    Tick(w, 10, 2e-5f);

    var store = run.Store;
    double ambient = PipeFlowStore.AmbientPressurePa(0.5);
    double expected = GasOrifice.Flow(
      3 * Atm,
      ambient,
      293.15,
      AirConstant,
      FlowGas.Air,
      Math.PI / 4 * 0.25 * 0.25
    );
    Assert.Equal(2, FlowingOpenings(store));
    for (int o = 0; o < store.OpeningCount; o++)
      if (Math.Abs(store.OpeningFlowKgPerSecond(o)) > 1e-6)
        Assert.InRange(store.OpeningFlowKgPerSecond(o) / expected, 0.98, 1.02);
  }

  // Fails when the chunk check is dropped from the face rule.
  [Fact]
  public void Fog_FaceTowardAnUnloadedChunkStaysShut() {
    var w = NewWorld();
    Lay(w, 30, Entry(3));
    Lay(w, 31, Entry(3));
    w.UnloadChunkAt(Cell(32));
    var run = RunAt(w, 30);

    Tick(w, 30, 2e-5f);

    var store = run.Store;
    Assert.Equal(1, FlowingOpenings(store));
    int far = store.FindOpening(
      store.IndexOf(Key(31)),
      (byte)(1 + BlockFacing.EAST.Index)
    );
    Assert.True(far >= 0);
    Assert.Equal(0, store.OpeningFlowKgPerSecond(far));
  }

  // Fails when Drop does not book the dropped content to the opening ledger.
  [Fact]
  public void Removal_BooksTheCellToTheAtmosphere() {
    var w = NewWorld();
    for (int x = 20; x < 23; x++)
      Lay(w, x, Entry(2));
    var run = RunAt(w, 20);
    Tick(w, 20);
    var store = run.Store;
    double Held() =>
      store.TotalGasKg()
      + store.TotalLiquidKg()
      - store.OpeningLedgerKg
      - store.SourceLedgerKg;
    double before = Held();
    double ledger = store.OpeningLedgerKg;
    double cellKg = store.GasKg(store.IndexOf(Key(22)));

    RemoveCell(w, 22);
    Tick(w, 20);

    store = RunAt(w, 20).Store;
    Assert.Equal(2, store.NodeCount);
    Assert.Equal(before, Held(), 9);
    Assert.Equal(ledger - cellKg, store.OpeningLedgerKg, 9);
    int face = store.FindOpening(
      store.IndexOf(Key(21)),
      (byte)(1 + BlockFacing.EAST.Index)
    );
    Assert.True(face >= 0);
    Assert.True(store.OpeningFlowKgPerSecond(face) != 0);
  }

  // Fails when the edge is rated by the stronger end instead of the weaker.
  [Fact]
  public void Edge_IsRatedByTheWeakerEnd() {
    var w = NewWorld();
    Lay(w, 40, Entry(2));
    Lay(w, 41, Entry(2, WeakMain), new WeakCellEntity());
    var store = RunAt(w, 40).Store;

    Tick(w, 40);

    int edge = store.EdgeBetween(
      store.IndexOf(Key(40)),
      store.IndexOf(Key(41))
    );
    Assert.InRange(store.Load(edge), 0.49, 0.5);
  }

  // Fails when the friction overload accepts a factor of zero.
  [Fact]
  public void AddEdge_RefusesANonPositiveFriction() {
    var store = new PipeFlowStore();
    int a = store.AddNode(Key(0), 1, 0, 0, 20);
    int b = store.AddNode(Key(1), 1, 0, 0, 20);

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.AddEdge(a, b, 0.25, 1, 0, FlowPipeClass.Cast)
    );
  }

  // Fails when OnMerge does not carry the keys the absorbed run built.
  [Fact]
  public void Merge_CarriesTheBuiltCellsSoALaterRemovalDropsThem() {
    var w = NewWorld();
    Lay(w, 20);
    Lay(w, 22);
    Tick(w, 20);
    Tick(w, 22);
    Lay(w, 21);
    Tick(w, 21);
    Assert.Equal(3, RunAt(w, 21).Store.NodeCount);
    Assert.Equal(6, RunAt(w, 21).Store.OpeningCount);

    RemoveCell(w, 22);
    Tick(w, 20);
    Assert.Equal(2, RunAt(w, 20).Store.NodeCount);

    RemoveCell(w, 20);
    Tick(w, 21);
    Assert.Equal(1, RunAt(w, 21).Store.NodeCount);
  }

  // Fails when a split does not hand each fragment the keys it built.
  [Fact]
  public void Split_CarriesTheBuiltCellsToTheirFragment() {
    var w = NewWorld();
    for (int x = 20; x < 25; x++)
      Lay(w, x);
    Tick(w, 20);
    Assert.Equal(5, RunAt(w, 20).Store.NodeCount);

    RemoveCell(w, 22);
    Tick(w, 23);
    RemoveCell(w, 24);
    Tick(w, 23);

    Assert.Equal(1, RunAt(w, 23).Store.NodeCount);
  }

  // Fails when the node is built ambient, ignoring the loaded entry.
  [Fact]
  public void Reload_RestoresTheCellFromItsEntry() {
    var first = NewWorld();
    var entry = Entry(3);
    Assert.True(PipeFlowSave.TryRead(entry, 0, out var original));
    Lay(first, 5, entry);
    Tick(first, 5);
    var saved = new TreeAttribute();
    first.GetBlockEntity(Cell(5))!.ToTreeAttributes(saved);
    Assert.True(PipeFlowSave.TryRead(saved, 0, out var live));
    Assert.Equal(original.GasKg, live.GasKg, 9);

    var second = NewWorld();
    Lay(second, 5, saved);
    Tick(second, 5);

    var store = RunAt(second, 5).Store;
    Assert.Equal(original.GasKg, store.GasKg(store.IndexOf(Key(5))), 9);
  }

  // Fails when ToTreeAttributes drops the entry the cell loaded and no run has used.
  [Fact]
  public void Reload_BeforeTheFirstTickKeepsTheEntry() {
    var w = NewWorld();
    var entry = Entry(3);
    Lay(w, 5, entry);

    var saved = new TreeAttribute();
    w.GetBlockEntity(Cell(5))!.ToTreeAttributes(saved);

    Assert.True(PipeFlowSave.TryRead(saved, 0, out var kept));
    Assert.True(PipeFlowSave.TryRead(entry, 0, out var original));
    Assert.Equal(original.GasKg, kept.GasKg, 12);
  }

  // Fails when Initialize keeps the loaded entry although the run already holds the node.
  [Fact]
  public void Reload_InALiveRunSpendsTheEntry() {
    var w = NewWorld();
    Lay(w, 5, Entry(3));
    Tick(w, 5);

    var fresh = (FlowCellEntity)w.Reload(Cell(5))!;

    Assert.Null(fresh.GetBehavior<BEBehaviorPipeFlowCell>().TakeLoaded());
  }

  // Fails when the cell writes a node although its network type is not pipeflow.
  [Fact]
  public void LumpedCell_WritesNoNode() {
    var w = NewWorld();
    var be = new LumpedCellEntity();
    w.Place(Cell(5), TestNetworkBlock.Create("pipe", "we", 2), be);
    be.GetBehavior<BEBehaviorPipeFlowCell>()
      .FromTreeAttributes(Entry(3), w.World);
    w.Initialize(be);

    var saved = new TreeAttribute();
    be.ToTreeAttributes(saved);

    Assert.False(PipeFlowSave.TryRead(saved, 0, out _));
  }
}
