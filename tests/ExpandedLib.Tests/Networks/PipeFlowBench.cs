using System;
using System.Diagnostics;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>Measures the pipe-flow step on a generated plant; a measurement, not a gate.</summary>
public class PipeFlowBench(ITestOutputHelper output) {
  private const int Nodes = 2100;
  private const int WarmSteps = 50;
  private const int TimedSteps = 400;

  // Fails when the generated plant does not step to finite pressures or loses mass beyond its ledgers.
  [Fact]
  public void Bench_2000Nodes() {
    var rng = new Random(5);
    var net = new PipeFlowNetwork(null!);
    PipeFlowStore store = net.Store;
    for (int i = 0; i < Nodes; i++)
      store.AddNode(
        NodeKey.Centre(new BlockPos(i, 0, 0)),
        0.0005 + rng.NextDouble() * 0.002,
        rng.Next(0, 6),
        0.0005 + rng.NextDouble() * 0.0015,
        150
      );
    for (int i = 1; i < Nodes; i++)
      store.AddEdge(
        Math.Max(0, i - 1 - rng.Next(0, 3)),
        i,
        0.05,
        1,
        (FlowPipeClass)rng.Next(0, 4)
      );
    for (int i = 0; i < 40; i++) {
      store.SetSource(rng.Next(0, Nodes), 0.02);
      store.AddOpening(rng.Next(0, Nodes), 0.0002, 101325, FlowGas.Steam);
    }
    double start = store.TotalGasKg();

    for (int i = 0; i < WarmSteps; i++)
      Tick(net);
    long begin = Stopwatch.GetTimestamp();
    for (int i = 0; i < TimedSteps; i++)
      Tick(net);
    double ns = Stopwatch.GetElapsedTime(begin).TotalMilliseconds * 1e6;

    output.WriteLine(
      $"{Nodes} nodes, {store.EdgeCount} edges, {store.OpeningCount} openings: {ns / TimedSteps / Nodes:F1} ns per node per step, {ns / TimedSteps / 1e6:F3} ms per step."
    );
    for (int i = 0; i < Nodes; i++)
      Assert.True(double.IsFinite(store.PublishedPressurePa(i)));
    Assert.Equal(
      start + store.SourceLedgerKg + store.OpeningLedgerKg,
      store.TotalGasKg(),
      6
    );
  }

  private static void Tick(PipeFlowNetwork net) {
    net.OnTick(null!, 0.2f, null!);
    net.OnTickPublished(null!, null!);
  }
}
