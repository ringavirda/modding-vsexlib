using System;
using System.Collections.Generic;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the physical pipe network's store, edge law and implicit solve: <see cref="PipeFlowStore"/>,
/// <see cref="PipeFlowSolver"/> and <see cref="PipeFlowNetwork"/>.
/// </summary>
public class PipeFlowTests {
  #region Fixtures

  private const double Volume = 0.02;
  private const double Bore = 0.05;

  private static int Add(
    PipeFlowStore store,
    int index,
    double gasKg,
    double height = 0,
    double volume = Volume
  ) =>
    store.AddNode(
      NodeKey.Centre(new BlockPos(index, 0, 0)),
      volume,
      height,
      gasKg,
      20
    );

  private static PipeFlowStore Chain(
    int count,
    double gasKg,
    FlowPipeClass pipeClass = FlowPipeClass.Cast
  ) {
    var store = new PipeFlowStore();
    for (int i = 0; i < count; i++)
      Add(store, i, gasKg);
    for (int i = 1; i < count; i++)
      store.AddEdge(i - 1, i, Bore, 1, pipeClass);
    return store;
  }

  #endregion

  #region Conservation

  // Fails when an edge's mass is debited from the old pressures' flow but credited from the new pressures' flow.
  [Fact]
  public void Conservation_StoresPlusLedgersConstant() {
    var rng = new Random(7);
    var store = new PipeFlowStore();
    const int nodes = 50;
    for (int i = 0; i < nodes; i++)
      Add(store, i, 0.001 + rng.NextDouble() * 0.01, rng.Next(0, 4));
    for (int i = 1; i < nodes; i++)
      store.AddEdge(
        rng.Next(0, i),
        i,
        0.02 + rng.NextDouble() * 0.1,
        0.5 + rng.NextDouble() * 4,
        (FlowPipeClass)rng.Next(0, 4)
      );
    for (int i = 0; i < 8; i++) {
      int a = rng.Next(0, nodes);
      int b = (a + 1 + rng.Next(0, nodes - 1)) % nodes;
      store.AddEdge(a, b, 0.05, 3, FlowPipeClass.Plated);
    }
    double start = store.TotalGasKg();

    for (int step = 0; step < 500; step++) {
      for (int i = 0; i < 6; i++)
        store.SetSource(
          rng.Next(0, nodes),
          (rng.NextDouble() - 0.5) * 0.05
        );
      store.Step(0.2);
      Assert.Equal(start + store.SourceLedgerKg, store.TotalGasKg(), 9);
      for (int i = 0; i < nodes; i++)
        Assert.True(store.GasKg(i) >= 0);
    }
    Assert.NotEqual(0d, store.SourceLedgerKg);
  }

  // Fails when a node that would be drained below zero is not limited, or is limited without
  // scaling the edge flows it feeds.
  [Fact]
  public void Drain_NeverTakesANodeBelowZero() {
    PipeFlowStore store = Chain(3, 0.0005);
    double start = store.TotalGasKg();
    store.SetSource(0, -5);
    store.Step(0.4);

    for (int i = 0; i < 3; i++)
      Assert.True(store.GasKg(i) >= 0);
    Assert.Equal(start + store.SourceLedgerKg, store.TotalGasKg(), 12);
    Assert.True(store.SourceLedgerKg < 0);
  }

  #endregion

  #region Order independence

  // Fails when a source declaration takes effect at once instead of at the next step.
  [Fact]
  public void OrderIndependence_PermutedMachineTicks() {
    double[] first = RunMachines(feederFirst: true);
    double[] second = RunMachines(feederFirst: false);

    Assert.Equal(first, second);
    Assert.NotEqual(first[0], first[^4]);
  }

  private static double[] RunMachines(bool feederFirst) {
    PipeFlowStore store = Chain(4, 0.001);
    void Feeder() => store.SetSource(0, 0.05);
    void Follower() => store.SetSource(3, -store.ActiveSource(0));
    var gas = new List<double>();
    for (int step = 0; step < 20; step++) {
      if (feederFirst) {
        Feeder();
        Follower();
      } else {
        Follower();
        Feeder();
      }
      store.Step(0.2);
      for (int i = 0; i < 4; i++)
        gas.Add(store.GasKg(i));
    }
    return [.. gas];
  }

  #endregion

  #region Edge law

  // Fails when the flow is not the rule s A sqrt(2 rho D / (f L)) dh / sqrt(max(|dh|, 1 Pa)) from the pressures the step starts with.
  [Fact]
  public void EdgeLaw_MatchesTheRule() {
    var store = new PipeFlowStore();
    Add(store, 0, 50, volume: 1000);
    Add(store, 1, 45, volume: 1000);
    int edge = store.AddEdge(0, 1, 0.1, 4, FlowPipeClass.Plated, 0.5);
    double dh = store.PressurePa(0) - store.PressurePa(1);
    double rho = 50d / 1000;
    double expected =
      0.5
      * Math.PI
      * 0.1
      * 0.1
      / 4
      * Math.Sqrt(2 * rho * 0.1 / (0.025 * 4))
      * dh
      / Math.Sqrt(Math.Max(Math.Abs(dh), 1));

    store.Step(0.01);

    Assert.Equal(expected, store.FlowKgPerSecond(edge), expected * 1e-3);
  }

  // Fails when the friction factors are not 0.03 cast, 0.025 plated, 0.02 rolled and wrought.
  [Theory]
  [InlineData((int)FlowPipeClass.Cast, 0.03)]
  [InlineData((int)FlowPipeClass.Plated, 0.025)]
  [InlineData((int)FlowPipeClass.Rolled, 0.02)]
  [InlineData((int)FlowPipeClass.Wrought, 0.02)]
  public void Friction_ByClass(int pipeClass, double friction) {
    Assert.Equal(friction, ((FlowPipeClass)pipeClass).Friction());
    Assert.Throws<ArgumentOutOfRangeException>(
      () => ((FlowPipeClass)99).Friction()
    );
  }

  // Fails when the friction factor does not scale the flow as 1 / sqrt(f).
  [Fact]
  public void Flow_FallsWithFriction() {
    double Flow(FlowPipeClass pipeClass) {
      var store = new PipeFlowStore();
      Add(store, 0, 50, volume: 1000);
      Add(store, 1, 45, volume: 1000);
      int edge = store.AddEdge(0, 1, 0.1, 4, pipeClass);
      store.Step(0.01);
      return store.FlowKgPerSecond(edge);
    }

    Assert.Equal(
      Math.Sqrt(0.03 / 0.02),
      Flow(FlowPipeClass.Rolled) / Flow(FlowPipeClass.Cast),
      3
    );
  }

  // Fails when a closed share still passes flow, or the flow runs against the drop.
  [Fact]
  public void Flow_RunsDownTheDropAndStopsAtShareZero() {
    PipeFlowStore store = Chain(2, 0.001);
    store.SetSource(1, 0.2);
    store.Step(0.2);
    Assert.True(store.FlowKgPerSecond(0) < 0);

    store.SetShare(0, 0);
    double before = store.GasKg(0);
    store.Step(0.2);
    Assert.Equal(0d, store.FlowKgPerSecond(0));
    Assert.Equal(before, store.GasKg(0));
  }

  // Fails when the gravity term is dropped from the drop or has the wrong sign.
  [Fact]
  public void Head_StandingGasSettlesWithHeight() {
    var store = new PipeFlowStore();
    Add(store, 0, 1, height: 0, volume: 1);
    Add(store, 1, 1, height: 10, volume: 1);
    int edge = store.AddEdge(0, 1, 0.1, 1, FlowPipeClass.Cast);

    store.Step(0.01);

    Assert.True(store.FlowKgPerSecond(edge) < 0);
  }

  #endregion

  #region Implicit solve

  // Fails when the step is not stable at a step far past the node time constant.
  [Fact]
  public void Step_IsStableFarPastTheTimeConstant() {
    PipeFlowStore store = Chain(30, 0.0001);
    store.SetSource(0, 0.5);
    for (int step = 0; step < 20; step++)
      store.Step(1000);

    double lowest = double.MaxValue;
    double highest = double.MinValue;
    for (int i = 0; i < 30; i++) {
      lowest = Math.Min(lowest, store.PressurePa(i));
      highest = Math.Max(highest, store.PressurePa(i));
    }
    Assert.True(double.IsFinite(lowest) && double.IsFinite(highest));
    Assert.True(lowest > 0);
    Assert.Equal(store.PressurePa(0), highest, 1e-6 * highest);
    Assert.Equal(store.SourceLedgerKg, store.TotalGasKg() - 30 * 0.0001, 9);
  }

  // Fails when fill from eliminating a node on a loop is not carried into the factorisation.
  [Fact]
  public void Solver_MatchesDenseSolveOnALoop() {
    var edges = new (int A, int B)[] {
      (0, 1), (1, 2), (2, 3), (3, 4), (4, 0), (1, 3), (5, 2), (5, 4),
    };
    const int n = 6;
    int[] from = [.. Array.ConvertAll(edges, e => e.A)];
    int[] to = [.. Array.ConvertAll(edges, e => e.B)];
    var rng = new Random(3);
    var dense = new double[n, n];
    var solver = new PipeFlowSolver();
    solver.SetTopology(n, from, to, edges.Length);
    solver.Begin();
    for (int i = 0; i < n; i++) {
      double d = 1 + rng.NextDouble();
      dense[i, i] += d;
      solver.AddDiagonal(i, d);
    }
    for (int e = 0; e < edges.Length; e++) {
      double ab = -rng.NextDouble();
      double ba = -rng.NextDouble();
      dense[from[e], from[e]] -= ab;
      dense[to[e], to[e]] -= ba;
      dense[from[e], to[e]] += ab;
      dense[to[e], from[e]] += ba;
      solver.AddEdge(e, ab, ba);
      solver.AddDiagonal(from[e], -ab);
      solver.AddDiagonal(to[e], -ba);
    }
    double[] rhs = [1, 2, 3, 4, 5, 6];
    double[] expected = DenseSolve(dense, rhs);

    solver.Solve(rhs);

    for (int i = 0; i < n; i++)
      Assert.Equal(expected[i], rhs[i], 9);
  }

  private static double[] DenseSolve(double[,] a, double[] b) {
    int n = b.Length;
    var m = (double[,])a.Clone();
    var x = (double[])b.Clone();
    for (int k = 0; k < n; k++)
      for (int i = k + 1; i < n; i++) {
        double l = m[i, k] / m[k, k];
        for (int j = k; j < n; j++)
          m[i, j] -= l * m[k, j];
        x[i] -= l * x[k];
      }
    for (int i = n - 1; i >= 0; i--) {
      for (int j = i + 1; j < n; j++)
        x[i] -= m[i, j] * x[j];
      x[i] /= m[i, i];
    }
    return x;
  }

  // Fails when the elimination order is rebuilt on every step, or kept after a node or edge is added.
  [Fact]
  public void EliminationOrder_IsCachedUntilTheTopologyChanges() {
    PipeFlowStore store = Chain(5, 0.001);
    store.Step(0.2);
    store.Step(0.2);
    store.SetShare(0, 0.5);
    store.Step(0.2);
    Assert.Equal(1, store.OrderBuilds);

    int added = Add(store, 9, 0.001);
    store.AddEdge(4, added, Bore, 1, FlowPipeClass.Cast);
    store.Step(0.2);
    Assert.Equal(2, store.OrderBuilds);
    Assert.Equal(5, store.IndexOf(NodeKey.Centre(new BlockPos(9, 0, 0))) );
  }

  #endregion

  #region Network

  // Fails when the network type, interval or tick step is not the pipeflow run's.
  [Fact]
  public void Network_StepsItsStoreOnTheListener() {
    var net = new PipeFlowNetwork(null!);
    Add(net.Store, 0, 0.002);
    Add(net.Store, 1, 0.001);
    net.Store.AddEdge(0, 1, Bore, 1, FlowPipeClass.Cast);

    net.OnTick(null!, 0.2f, null!);

    Assert.Equal("pipeflow", net.NetworkType);
    Assert.Equal(200, net.StepIntervalMs);
    Assert.True(net.Store.FlowKgPerSecond(0) > 0);
  }

  // Fails when a pipeflow run merges or splits as if it supported it.
  [Fact]
  public void Network_RefusesMergeAndSplit() {
    var net = new PipeFlowNetwork(null!);
    Assert.Throws<NotSupportedException>(() => net.OnMerge(net, null!));
    Assert.Throws<NotSupportedException>(
      () => net.OnSplitFragment(net, (Vintagestory.API.Common.IBlockAccessor)null!)
    );
  }

  // Fails when a duplicate key or an out-of-range value is accepted.
  [Fact]
  public void Store_RejectsBadInput() {
    var store = new PipeFlowStore();
    Add(store, 0, 0.001);
    Assert.Throws<ArgumentException>(() => Add(store, 0, 0.001));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => Add(store, 1, 0.001, volume: 0)
    );
    Assert.Throws<ArgumentOutOfRangeException>(
      () => store.AddEdge(0, 0, Bore, 1, FlowPipeClass.Cast)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() => store.Step(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => store.SetShare(0, 0.5));
  }

  #endregion
}
