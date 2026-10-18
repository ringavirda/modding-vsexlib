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
        store.SetSource(rng.Next(0, nodes), (rng.NextDouble() - 0.5) * 0.05);
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
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      ((FlowPipeClass)99).Friction()
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

  // Fails when the flowing density is taken from the side of the bare pressure difference instead of the side the
  // height-corrected drop flows from.
  [Fact]
  public void EdgeLaw_UsesTheDensityOfTheSideTheDropFlowsFrom() {
    var store = new PipeFlowStore();
    store.AddNode(NodeKey.Centre(new BlockPos(0, 0, 0)), 1000, 0, 1000, 400);
    store.AddNode(NodeKey.Centre(new BlockPos(1, 0, 0)), 1000, 3000, 2200, 20);
    int edge = store.AddEdge(0, 1, 0.1, 4, FlowPipeClass.Plated, 0.5);
    double dp = store.PressurePa(0) - store.PressurePa(1);
    double rho = 2.2;
    double drop = dp + rho * 9.80665 * (0 - 3000);
    Assert.True(dp > 0);
    Assert.True(drop < 0);
    double expected =
      0.5
      * Math.PI
      * 0.1
      * 0.1
      / 4
      * Math.Sqrt(2 * rho * 0.1 / (0.025 * 4))
      * drop
      / Math.Sqrt(Math.Abs(drop));

    store.Step(0.01);

    Assert.Equal(
      expected,
      store.FlowKgPerSecond(edge),
      Math.Abs(expected) * 1e-2
    );
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
    var edges = new (int A, int B)[]
    {
      (0, 1),
      (1, 2),
      (2, 3),
      (3, 4),
      (4, 0),
      (1, 3),
      (5, 2),
      (5, 4),
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
    Assert.Equal(5, store.IndexOf(NodeKey.Centre(new BlockPos(9, 0, 0))));
  }

  #endregion

  #region Openings

  private const double Atm = 101325;

  private static int AddAtPressure(
    PipeFlowStore store,
    int index,
    double pressurePa,
    double temperatureC = 20,
    double gasConstant = 461.5,
    double volume = 1000
  ) =>
    store.AddNode(
      NodeKey.Centre(new BlockPos(index, 0, 0)),
      volume,
      0,
      pressurePa * volume / (gasConstant * (temperatureC + 273.15)),
      temperatureC,
      gasConstant
    );

  private static double Nozzle(
    double upstreamPa,
    double downstreamPa,
    double kelvin,
    double gasConstant,
    double k,
    double area
  ) {
    double r = downstreamPa / upstreamPa;
    return 0.97
      * area
      * upstreamPa
      * Math.Sqrt(
        2
          * k
          / ((k - 1) * gasConstant * kelvin)
          * (Math.Pow(r, 2 / k) - Math.Pow(r, (k + 1) / k))
      );
  }

  // Fails when the choke test is inverted, so a node at 5 atm is not passed by Napier's 1.457e-3 A P.
  [Fact]
  public void Napier_OpenEndMatchesRule() {
    var store = new PipeFlowStore();
    const double area = 0.05 * 0.05 * Math.PI / 4;
    AddAtPressure(store, 0, 5 * Atm);
    int open = store.AddOpening(0, area, Atm, FlowGas.Steam);
    double p = store.PressurePa(0);

    store.Step(0.01);

    double expected = 1.457e-3 * area * p;
    Assert.Equal(expected, store.OpeningFlowKgPerSecond(open), expected * 0.02);
  }

  // Fails when the flow below the choke ratio is not the isentropic nozzle form.
  [Fact]
  public void Opening_BelowChokingFollowsTheNozzleForm() {
    var store = new PipeFlowStore();
    const double area = 0.002;
    AddAtPressure(store, 0, 1.5 * Atm);
    int open = store.AddOpening(0, area, Atm, FlowGas.Steam);
    double p = store.PressurePa(0);

    store.Step(0.01);

    double expected = Nozzle(p, Atm, 293.15, 461.5, 1.3, area);
    Assert.Equal(expected, store.OpeningFlowKgPerSecond(open), expected * 0.02);
  }

  // Fails when the air coefficient leaves out the temperature factor sqrt(293 / T).
  [Fact]
  public void Opening_AirChokedScalesWithTemperature() {
    var store = new PipeFlowStore();
    const double area = 0.002;
    AddAtPressure(store, 0, 5 * Atm, temperatureC: 126.85, gasConstant: 287);
    int open = store.AddOpening(0, area, Atm, FlowGas.Air);
    double p = store.PressurePa(0);

    store.Step(0.01);

    double expected = 2.3e-3 * area * p * Math.Sqrt(293 / 400d);
    Assert.Equal(expected, store.OpeningFlowKgPerSecond(open), expected * 0.02);
  }

  // Fails when a node below the outside pressure vents instead of drawing air in, or the inflow is not the choked or nozzle rule.
  [Fact]
  public void Opening_BelowOutsideDrawsAirIn() {
    const double area = 0.002;
    var choked = new PipeFlowStore();
    AddAtPressure(choked, 0, 0.4 * Atm);
    int a = choked.AddOpening(0, area, Atm, FlowGas.Steam);
    double before = choked.GasKg(0);
    choked.Step(0.01);
    double expectedChoked = 2.3e-3 * area * Atm;
    Assert.Equal(
      -expectedChoked,
      choked.OpeningFlowKgPerSecond(a),
      expectedChoked * 0.02
    );
    Assert.True(choked.GasKg(0) > before);
    Assert.True(choked.OpeningLedger(a) > 0);

    var gentle = new PipeFlowStore();
    AddAtPressure(gentle, 0, 0.8 * Atm);
    int b = gentle.AddOpening(0, area, Atm, FlowGas.Steam);
    double p = gentle.PressurePa(0);
    gentle.Step(0.01);
    double expectedGentle = Nozzle(Atm, p, 293.15, 287, 1.4, area);
    Assert.Equal(
      -expectedGentle,
      gentle.OpeningFlowKgPerSecond(b),
      expectedGentle * 0.02
    );
  }

  // Fails when an opening takes gas out of a node without crediting its ledger, a closed area still passes flow, or an opening's outflow is left out of the limit that keeps a node from passing more than it holds.
  [Fact]
  public void Conservation_OpeningsCloseTheBalance() {
    var rng = new Random(11);
    PipeFlowStore store = Chain(12, 0.01);
    var openings = new[]
    {
      store.AddOpening(0, 0.0005, Atm, FlowGas.Steam),
      store.AddOpening(7, 0.0003, 0.5 * Atm, FlowGas.Steam),
      store.AddOpening(11, 0.0008, 2 * Atm, FlowGas.Steam),
    };
    double start = store.TotalGasKg();

    for (int step = 0; step < 300; step++) {
      store.SetSource(rng.Next(0, 12), (rng.NextDouble() - 0.4) * 0.02);
      store.Step(0.2);
      Assert.Equal(
        start + store.SourceLedgerKg + store.OpeningLedgerKg,
        store.TotalGasKg(),
        9
      );
    }
    double sum = 0;
    foreach (int o in openings)
      sum += store.OpeningLedger(o);
    Assert.Equal(store.OpeningLedgerKg, sum, 12);
    Assert.NotEqual(0d, store.OpeningLedgerKg);

    store.SetOpeningArea(openings[0], 0);
    store.Step(0.2);
    Assert.Equal(0d, store.OpeningFlowKgPerSecond(openings[0]));
  }

  // Fails when a negative area or outside pressure, or a missing node or opening, is accepted.
  [Fact]
  public void Openings_RejectBadInput() {
    var store = new PipeFlowStore();
    Add(store, 0, 0.001);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.AddOpening(3, 0.001, Atm, FlowGas.Air)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.AddOpening(0, -1, Atm, FlowGas.Air)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.AddOpening(0, 0.001, -1, FlowGas.Air)
    );
    int open = store.AddOpening(0, 0.001, Atm, FlowGas.Air);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.SetOpeningArea(open, -1)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.SetOpeningArea(5, 1)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.OpeningFlowKgPerSecond(5)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() => store.OpeningLedger(5));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.PublishedOpeningFlowKgPerSecond(5)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.PublishedOpeningLedger(5)
    );
  }

  #endregion

  #region Conductance limits

  private static PipeFlowStore Equalising(out int edge) {
    var store = new PipeFlowStore();
    AddAtPressure(store, 0, 2 * Atm, volume: 0.001);
    AddAtPressure(store, 1, Atm, volume: 0.001);
    edge = store.AddEdge(0, 1, 0.1, 4, FlowPipeClass.Plated);
    return store;
  }

  // Fails when a conductance may move by more than a factor 4 in one step.
  [Fact]
  public void Step_LimitsConductanceToAFactorOfFour() {
    PipeFlowStore store = Equalising(out int edge);
    store.Step(100);
    double first = store.UnitConductance(edge);

    store.Step(100);

    Assert.Equal(4 * first, store.UnitConductance(edge), first * 1e-9);
  }

  // Fails when the second linearisation is skipped after a conductance moved by more than a factor 2, or run when none did.
  [Fact]
  public void Step_RepassesWhenAConductanceMoved() {
    PipeFlowStore store = Equalising(out _);
    store.Step(100);
    Assert.Equal(1, store.LastStepPasses);
    store.Step(100);
    Assert.Equal(2, store.LastStepPasses);

    var calm = new PipeFlowStore();
    Add(calm, 0, 50, volume: 1000);
    Add(calm, 1, 45, volume: 1000);
    calm.AddEdge(0, 1, 0.1, 4, FlowPipeClass.Plated);
    calm.Step(0.01);
    calm.Step(0.01);
    Assert.Equal(1, calm.LastStepPasses);
  }

  #endregion

  #region Sleep

  // Fails when a settled run keeps stepping, a changed share or source does not wake it, or a run with a constant nonzero declaration rests.
  [Fact]
  public void Sleep_RestsAndWakes() {
    PipeFlowStore store = Chain(3, 0.001);
    store.AddNode(NodeKey.Centre(new BlockPos(9, 0, 0)), Volume, 0, 0.004, 20);
    store.AddEdge(2, 3, Bore, 1, FlowPipeClass.Cast);
    for (int i = 0; i < 40 && !store.Asleep; i++)
      store.Step(10);
    Assert.True(store.Asleep);

    int solved = store.StepsSolved;
    double gas = store.GasKg(0);
    store.Step(10);
    Assert.Equal(solved, store.StepsSolved);
    Assert.Equal(gas, store.GasKg(0));

    store.SetShare(0, 0.5);
    Assert.False(store.Asleep);
    store.Step(10);
    Assert.Equal(solved + 1, store.StepsSolved);
    for (int i = 0; i < 40 && !store.Asleep; i++)
      store.Step(10);
    Assert.True(store.Asleep);

    store.SetSource(0, 0);
    Assert.True(store.Asleep);
    store.SetSource(0, 0.001);
    Assert.False(store.Asleep);

    PipeFlowStore held = Chain(2, 0);
    held.SetSource(0, -0.01);
    for (int i = 0; i < 6; i++) {
      held.Step(0.2);
      Assert.False(held.Asleep);
    }
    Assert.Equal(6, held.StepsSolved);
    held.SetSource(0, 0);
    held.Step(0.2);
    Assert.True(held.Asleep);
  }

  // Fails when a run with an opening still passing flow is put to sleep, or a changed opening area does not wake it.
  [Fact]
  public void Sleep_OpenEndKeepsStepping() {
    var store = new PipeFlowStore();
    AddAtPressure(store, 0, 3 * Atm);
    int open = store.AddOpening(0, 0.00001, Atm, FlowGas.Steam);
    for (int i = 0; i < 5; i++)
      store.Step(0.2);
    Assert.False(store.Asleep);

    store.SetOpeningArea(open, 0);
    store.Step(0.2);
    store.Step(0.2);
    Assert.True(store.Asleep);
    store.SetOpeningArea(open, 0.00001);
    Assert.False(store.Asleep);
  }

  #endregion

  #region Publish

  // Fails when the published values follow the live step instead of the last Publish, or Publish is not called after the tick.
  [Fact]
  public void Publish_ShowsTheLastPublishedStep() {
    var net = new PipeFlowNetwork(null!);
    PipeFlowStore store = net.Store;
    Add(store, 0, 0.002);
    Add(store, 1, 0.001);
    int edge = store.AddEdge(0, 1, Bore, 1, FlowPipeClass.Cast);
    int open = store.AddOpening(1, 0.0001, Atm, FlowGas.Steam);

    net.OnTick(null!, 0.2f, null!);
    Assert.Equal(0d, store.PublishedPressurePa(0));
    Assert.Equal(0d, store.PublishedFlowKgPerSecond(edge));
    Assert.Equal(0d, store.PublishedOpeningFlowKgPerSecond(open));
    Assert.Equal(0d, store.PublishedOpeningLedger(open));

    net.OnTickPublished(null!, null!);
    double pressure = store.PublishedPressurePa(0);
    Assert.Equal(store.PressurePa(0), pressure);
    Assert.Equal(
      store.FlowKgPerSecond(edge),
      store.PublishedFlowKgPerSecond(edge)
    );
    Assert.Equal(
      store.OpeningFlowKgPerSecond(open),
      store.PublishedOpeningFlowKgPerSecond(open)
    );
    Assert.Equal(store.OpeningLedger(open), store.PublishedOpeningLedger(open));
    Assert.NotEqual(0d, store.PublishedOpeningLedger(open));

    net.OnTick(null!, 0.2f, null!);
    Assert.NotEqual(store.PressurePa(0), pressure);
    Assert.Equal(pressure, store.PublishedPressurePa(0));
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
    Assert.Throws<NotSupportedException>(() =>
      net.OnSplitFragment(net, (Vintagestory.API.Common.IBlockAccessor)null!)
    );
  }

  // Fails when a duplicate key or an out-of-range value is accepted.
  [Fact]
  public void Store_RejectsBadInput() {
    var store = new PipeFlowStore();
    Add(store, 0, 0.001);
    Assert.Throws<ArgumentException>(() => Add(store, 0, 0.001));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      Add(store, 1, 0.001, volume: 0)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      store.AddEdge(0, 0, Bore, 1, FlowPipeClass.Cast)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() => store.Step(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => store.SetShare(0, 0.5));
  }

  #endregion
}
