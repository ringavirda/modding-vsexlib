using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers liquid and air in the physical pipe network: the hydrostatic head, the liquid-full branch, air in a
/// water node, drains and the atmosphere's head, in <see cref="PipeFlowStore"/>.
/// </summary>
public class PipeLiquidTests {
  #region Fixtures

  private const double Volume = 0.02;
  private const double Bore = 0.05;
  private const double FullKg = 0.99 * 1000 * Volume;
  private const double Atmosphere = 101325;
  private const double SteamConstant = 461.5;

  private static NodeKey Cell(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private static int Add(
    PipeFlowStore store,
    int index,
    double gasKg,
    double liquidKg = 0,
    double height = 0,
    double volume = Volume,
    double temperatureC = 20
  ) =>
    store.AddNode(
      Cell(index),
      volume,
      height,
      gasKg,
      temperatureC,
      SteamConstant,
      liquidKg,
      0,
      true
    );

  private static double SteamKg(double pressurePa, double volumeM3) =>
    pressurePa * volumeM3 / (SteamConstant * 293.15);

  private static void Run(PipeFlowStore store, int steps) {
    for (int i = 0; i < steps; i++)
      store.Step(0.2);
  }

  #endregion

  #region Head

  // Fails when the gravity term is dropped from the liquid edge law, so a sealed full column settles with equal pressure top and bottom.
  [Fact]
  public void Head_WaterColumn() {
    var store = new PipeFlowStore();
    for (int i = 0; i < 10; i++)
      Add(store, i, 0, FullKg * 1.005, i);
    for (int i = 1; i < 10; i++)
      store.AddEdge(i - 1, i, Bore, 1, FlowPipeClass.Cast);

    Run(store, 4000);

    Assert.All(
      Enumerable.Range(0, 10),
      i => Assert.True(store.IsLiquidFull(i))
    );
    double head = store.PressurePa(0) - store.PressurePa(9);
    Assert.InRange(head, 9 * 9807 * 0.99, 9 * 9807 * 1.01);
  }

  // Fails when the liquid-full branch is anchored at the atmosphere instead of the void branch's pressure at the switch, so the pressure jumps as the node fills past 99 %.
  [Fact]
  public void LiquidFull_ContinuousAtSwitch() {
    var store = new PipeFlowStore();
    double steam = SteamKg(5 * Atmosphere, 0.01 * Volume);
    int below = Add(store, 0, steam, FullKg * (1 - 1e-5));
    int above = Add(store, 1, steam, FullKg * (1 + 1e-5));

    Assert.False(store.IsLiquidFull(below));
    Assert.True(store.IsLiquidFull(above));
    double jump = store.PressurePa(above) - store.PressurePa(below);
    Assert.InRange(jump / store.PressurePa(below), 0, 0.01);
    Assert.InRange(store.PressurePa(below), 4.9 * Atmosphere, 5.1 * Atmosphere);
  }

  // Fails when a full node's capacity and flow weight stay those of its gas void, so a liquid flow moves the node's pressure by a factor of a thousand too little and the column never settles.
  [Fact]
  public void LiquidFull_PressureRisesWithTheLiquidAdded() {
    var store = new PipeFlowStore();
    int tank = Add(store, 0, 0, FullKg * 1.01);
    int vessel = Add(store, 1, 0, FullKg * 1.0);
    store.AddEdge(tank, vessel, Bore, 1, FlowPipeClass.Cast);
    double start = store.TotalLiquidKg();

    Run(store, 400);

    Assert.Equal(start, store.TotalLiquidKg(), 9);
    Assert.InRange(store.PressurePa(tank) - store.PressurePa(vessel), -10, 10);
    Assert.InRange(store.LiquidKg(vessel), FullKg * 1.004, FullKg * 1.006);
  }

  #endregion

  #region Air

  // Fails when air an opening lets in is kept as steam in a water node, so the condenser's air reads zero once the valves open.
  [Fact]
  public void Condenser_KeepsVacuumBesideOpenTap() {
    var store = new PipeFlowStore();
    double vacuum = SteamKg(0.2 * Atmosphere, 0.5);
    int condenser = Add(store, 0, vacuum, 0, 0, 0.5);
    int middle = Add(store, 1, SteamKg(0.2 * Atmosphere, Volume));
    int tap = Add(store, 2, SteamKg(0.2 * Atmosphere, Volume));
    int first = store.AddEdge(
      condenser,
      middle,
      Bore,
      1,
      FlowPipeClass.Cast,
      0
    );
    int second = store.AddEdge(middle, tap, Bore, 1, FlowPipeClass.Cast, 0);
    store.AddOpening(tap, 1e-4, Atmosphere, FlowGas.Steam);
    double held = store.PressurePa(condenser);

    Run(store, 300);

    Assert.True(store.AirKg(tap) > 0);
    Assert.InRange(store.PressurePa(tap), 0.95 * Atmosphere, 1.05 * Atmosphere);
    Assert.Equal(0, store.AirKg(condenser));
    Assert.Equal(held, store.PressurePa(condenser), 3);

    store.SetShare(first, 1);
    store.SetShare(second, 1);
    Run(store, 600);

    Assert.True(store.AirKg(condenser) > 0);
    Assert.True(store.PressurePa(condenser) > 0.9 * Atmosphere);
  }

  // Fails when a gas flow carries the node's steam alone and leaves its air behind, so the air never reaches the condenser.
  [Fact]
  public void Air_TravelsWithTheGasPhase() {
    var store = new PipeFlowStore();
    int source = store.AddNode(
      Cell(0),
      Volume,
      0,
      SteamKg(Atmosphere, Volume) * 0.5,
      20,
      SteamConstant,
      0,
      SteamKg(Atmosphere, Volume) * 0.5,
      true
    );
    int sink = Add(store, 1, 0);
    store.AddEdge(source, sink, Bore, 1, FlowPipeClass.Cast);
    double start = store.TotalGasKg();

    Run(store, 100);

    Assert.True(store.AirKg(sink) > 0);
    Assert.True(store.GasKg(sink) > 0);
    Assert.Equal(start, store.TotalGasKg(), 12);
    Assert.Equal(
      store.AirKg(source) / store.GasKg(source),
      store.AirKg(sink) / store.GasKg(sink),
      2
    );
  }

  #endregion

  #region Drains

  // Fails when a drain is treated as a gas opening, so the first step passes steam and leaves the liquid in the node.
  [Fact]
  public void Drain_PassesLiquidThenGas() {
    var store = new PipeFlowStore();
    double steam = SteamKg(3 * Atmosphere, Volume / 2);
    int node = Add(store, 0, steam, 10);
    store.AddOpening(node, 1e-4, Atmosphere, FlowGas.Steam, true);
    double start = store.TotalGasKg() + store.TotalLiquidKg();

    store.Step(0.2);

    Assert.True(store.LiquidKg(node) < 10);
    Assert.Equal(steam, store.GasKg(node), 12);
    Assert.True(store.OpeningFlowKgPerSecond(0) > 0);

    for (int i = 0; i < 2000 && store.LiquidKg(node) > 0; i++)
      store.Step(0.2);
    Assert.Equal(0, store.LiquidKg(node));
    double gasWhenDry = store.GasKg(node);
    Run(store, 50);

    Assert.True(store.GasKg(node) < gasWhenDry);
    Assert.Equal(
      start + store.OpeningLedgerKg,
      store.TotalGasKg() + store.TotalLiquidKg(),
      9
    );
  }

  // Fails when a liquid edge credits its destination with gas instead of liquid, so the liquid vanishes between nodes.
  [Fact]
  public void Liquid_ConservedAcrossEdgesAndDrain() {
    var store = new PipeFlowStore();
    for (int i = 0; i < 4; i++)
      Add(store, i, 0, FullKg * 1.005, 3 - i);
    for (int i = 1; i < 4; i++)
      store.AddEdge(i - 1, i, Bore, 1, FlowPipeClass.Cast);
    store.AddOpening(3, 5e-5, Atmosphere, FlowGas.Steam, true);
    double start = store.TotalGasKg() + store.TotalLiquidKg();

    for (int step = 0; step < 300; step++) {
      store.Step(0.2);
      Assert.Equal(
        start + store.OpeningLedgerKg,
        store.TotalGasKg() + store.TotalLiquidKg(),
        9
      );
    }

    Assert.True(store.OpeningLedgerKg < -0.1);
    Assert.True(store.LiquidKg(0) < FullKg * 1.005);
  }

  // Fails when a liquid flow advects temperature with the gas heat capacity instead of the liquid's, so a mixed node reads off the mass-weighted mean.
  [Fact]
  public void Liquid_AdvectsTheUpwindTemperature() {
    var store = new PipeFlowStore();
    int hot = Add(store, 0, 0, FullKg * 1.01, 0, Volume, 80);
    int cold = Add(store, 1, 0, FullKg, 0, Volume, 20);
    store.AddEdge(hot, cold, Bore, 1, FlowPipeClass.Cast);
    double before = store.LiquidKg(cold);

    store.Step(0.2);

    double moved = store.LiquidKg(cold) - before;
    Assert.True(moved > 0);
    Assert.Equal(80, store.TemperatureC(hot), 6);
    double mixed = (before * 20 + moved * 80) / (before + moved);
    Assert.Equal(mixed, store.TemperatureC(cold), 6);
  }

  #endregion

  #region Atmosphere

  // Fails when the atmosphere's head rises with height instead of falling, so a node opened at height 10 settles above one atmosphere.
  [Fact]
  public void AmbientPressure_FallsWithHeight() {
    Assert.Equal(Atmosphere, PipeFlowStore.AmbientPressurePa(0), 9);
    Assert.Equal(Atmosphere - 118, PipeFlowStore.AmbientPressurePa(10), 9);
    Assert.Equal(Atmosphere - 59, PipeFlowStore.AmbientPressurePa(10, 5), 9);

    var store = new PipeFlowStore();
    int node = store.AddNode(
      Cell(0),
      Volume,
      10,
      SteamKg(0.5 * Atmosphere, Volume),
      20
    );
    store.AddAtmosphereOpening(node, 1e-4, FlowGas.Air);

    Run(store, 600);

    Assert.Equal(Atmosphere - 118, store.PressurePa(node), 0);
  }

  #endregion

  #region Save

  // Fails when SaveNode writes zero air and liquid, so a reloaded full node comes back empty.
  [Fact]
  public void Reload_KeepsAirAndLiquid() {
    var w = new TestWorld();
    var live = new PipeFlowNetwork(w.Networks);
    live.Store.AddNode(
      Cell(0),
      Volume,
      0,
      0.001,
      20,
      SteamConstant,
      FullKg * 0.5,
      0.002,
      true
    );
    var host = new TreeAttribute();
    live.SaveNode(Cell(0), host);

    var rebuilt = new PipeFlowNetwork(w.Networks);
    Assert.True(rebuilt.RestoreNode(Cell(0), host, Volume, 0));

    int node = rebuilt.Store.IndexOf(Cell(0));
    Assert.Equal(FullKg * 0.5, rebuilt.Store.LiquidKg(node), 12);
    Assert.Equal(0.002, rebuilt.Store.AirKg(node), 12);
    Assert.Equal(live.Store.PressurePa(0), rebuilt.Store.PressurePa(node), 3);
  }

  #endregion
}
