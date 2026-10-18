using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// How the physical pipe run merges and splits as the graph does: stores union and partition by node,
/// and only equal or ambient media join.
/// </summary>
public class MergeSplitTests {
  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("pipeflow", sys => new PipeFlowNetwork(sys));
    return w;
  }

  private static BlockPos Cell(int x) => new(x, 0, 0);

  private static PipeFlowNetwork RunAt(TestWorld w, int x) =>
    (PipeFlowNetwork)w.NetworkAt(Cell(x))!;

  private static void Lay(TestWorld w, params int[] xs) {
    foreach (int x in xs)
      w.PlaceNode(Cell(x), "pipeflow", "we");
  }

  /// <summary>Gives each node of <paramref name="run"/> a node in its store, holding <c>0.001 * (x + 1)</c> kg, and joins neighbours along x.</summary>
  private static void Fill(PipeFlowNetwork run, double scale = 1) {
    foreach (var key in run.NodeKeys.OrderBy(k => k.Pos.X)) {
      int node = run.Store.AddNode(
        key,
        0.02,
        0,
        0.001 * (key.Pos.X + 1) * scale,
        20
      );
      if (node > 0)
        run.Store.AddEdge(node - 1, node, 0.05, 1, FlowPipeClass.Cast);
    }
  }

  private static double GasOf(TestWorld w) =>
    w
      .Networks.AllNetworks.OfType<PipeFlowNetwork>()
      .Sum(r => r.Store.TotalGasKg());

  // Fails when CopyFrom gives every node an even share of the source run's gas instead of its own.
  [Fact]
  public void Conservation_ValveTogglesAndBreaks() {
    var w = NewWorld();
    Lay(w, 0, 1, 2, 3, 4, 5);
    var run = RunAt(w, 0);
    run.Medium = FlowMedium.Water;
    Fill(run);
    double start = run.Store.TotalGasKg();

    for (int i = 0; i < 100; i++) {
      run.Store.SetShare(i % 5, i % 2);
      run.Store.Step(0.2);
    }

    Assert.Equal(start, run.Store.TotalGasKg(), 9);
    Assert.Same(run, RunAt(w, 5));

    double lost = run.Store.GasKg(run.Store.IndexOf(NodeKey.Centre(Cell(3))));
    double kept = run.Store.GasKg(run.Store.IndexOf(NodeKey.Centre(Cell(4))));
    w.RemoveNode(Cell(3));

    var near = RunAt(w, 1);
    var far = RunAt(w, 4);
    Assert.NotSame(near, far);
    Assert.Equal(3, near.Store.NodeCount);
    Assert.Equal(2, far.Store.NodeCount);
    Assert.Equal(1, far.Store.EdgeCount);
    Assert.Equal(FlowMedium.Water, far.Medium);
    Assert.Equal(start - lost, GasOf(w), 9);
    Assert.Equal(
      kept,
      far.Store.GasKg(far.Store.IndexOf(NodeKey.Centre(Cell(4)))),
      12
    );

    const double ambientAir = 0.0004;
    Lay(w, 3);
    var joined = RunAt(w, 3);
    joined.Store.AddNode(NodeKey.Centre(Cell(3)), 0.02, 0, ambientAir, 20);
    Assert.Equal(start - lost + ambientAir, GasOf(w), 9);
    for (int i = 0; i < 20; i++) {
      joined.Store.SetShare(0, 1);
      joined.Store.Step(0.2);
    }
    Assert.Equal(start - lost + ambientAir, GasOf(w), 9);
  }

  // Fails when CanMerge accepts any pair of media, so the air run joins the steam run.
  [Fact]
  public void Merge_DifferentMediaInventNothing() {
    var w = NewWorld();
    Lay(w, 0, 1);
    var steam = RunAt(w, 0);
    steam.Medium = FlowMedium.Water;
    Fill(steam);
    Lay(w, 3, 4);
    var air = RunAt(w, 3);
    air.Medium = FlowMedium.Air;
    Fill(air);
    double steamKg = steam.Store.TotalGasKg();
    double airKg = air.Store.TotalGasKg();

    Lay(w, 2);

    Assert.Equal(2, w.Networks.AllNetworks.Count());
    Assert.Equal(steamKg, steam.Store.TotalGasKg(), 12);
    Assert.Equal(airKg, air.Store.TotalGasKg(), 12);
    Assert.Equal(2, steam.Store.NodeCount);
    Assert.Equal(2, air.Store.NodeCount);
  }

  // Fails when a merge relabels the joined run with the other run's medium, whichever run took the other in.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void An_ambient_run_joins_a_steam_run_and_the_steam_keeps_its_medium(
    bool steamFirst
  ) {
    var w = NewWorld();
    Lay(w, 0, 1);
    Lay(w, 3, 4);
    var steam = RunAt(w, steamFirst ? 0 : 3);
    var ambient = RunAt(w, steamFirst ? 3 : 0);
    steam.Medium = FlowMedium.Water;
    Fill(steam);
    Fill(ambient);
    double total = steam.Store.TotalGasKg() + ambient.Store.TotalGasKg();

    Lay(w, 2);

    var run = RunAt(w, 2);
    Assert.Single(w.Networks.AllNetworks);
    Assert.Same(run, RunAt(w, 4));
    Assert.Equal(FlowMedium.Water, run.Medium);
    Assert.Equal(4, run.Store.NodeCount);
    Assert.Equal(total, run.Store.TotalGasKg(), 12);
  }

  // Fails when Absorb drops an opening's ledger or a node's freeze flag.
  [Fact]
  public void A_merge_keeps_ledgers_flags_and_sources() {
    var a = new PipeFlowStore();
    var b = new PipeFlowStore();
    int a0 = a.AddNode(NodeKey.Centre(Cell(0)), 0.02, 0, 0.004, 20);
    int b0 = b.AddNode(NodeKey.Centre(Cell(5)), 0.02, 0, 0.004, 20);
    int vent = b.AddOpening(b0, 0.0001, 101325, FlowGas.Steam);
    b.SetSource(b0, 0.0001);
    for (int i = 0; i < 5; i++)
      b.Step(0.2);
    b.SetFrozen(b0, true);
    double ledger = b.OpeningLedger(vent);
    Assert.NotEqual(0, ledger);

    a.Absorb(b);

    int moved = a.IndexOf(NodeKey.Centre(Cell(5)));
    Assert.Equal(1, a.OpeningCount);
    Assert.Equal(ledger, a.OpeningLedger(0), 12);
    Assert.True(a.IsFrozen(moved));
    Assert.False(a.IsFrozen(a0));
    Assert.Equal(b.GasKg(b0), a.GasKg(moved), 12);
    Assert.Equal(b.OpeningLedgerKg, a.OpeningLedgerKg, 12);
  }

  // Fails when Take copies an edge with only one end among the keys.
  [Fact]
  public void A_split_takes_only_the_edges_and_openings_inside_its_keys() {
    var whole = new PipeFlowStore();
    for (int x = 0; x < 4; x++)
      whole.AddNode(NodeKey.Centre(Cell(x)), 0.02, 0, 0.001, 20);
    whole.AddEdge(0, 1, 0.05, 1, FlowPipeClass.Cast);
    whole.AddEdge(1, 2, 0.05, 1, FlowPipeClass.Cast);
    whole.AddEdge(2, 3, 0.05, 1, FlowPipeClass.Cast);
    whole.AddOpening(0, 0.0001, 101325, FlowGas.Steam);
    whole.AddOpening(3, 0.0001, 101325, FlowGas.Steam);

    var part = new PipeFlowStore();
    part.Take(
      whole,
      new[] { NodeKey.Centre(Cell(0)), NodeKey.Centre(Cell(1)) }.ToHashSet()
    );

    Assert.Equal(2, part.NodeCount);
    Assert.Equal(1, part.EdgeCount);
    Assert.Equal(1, part.OpeningCount);
    Assert.Equal(4, whole.NodeCount);
    Assert.Equal(3, whole.EdgeCount);
  }
}
