using System;
using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// What machines declare at a port of the physical pipe run and read back: <see cref="FlowPort"/>,
/// its links and couplings, and the lumped network standing apart from it.
/// </summary>
public class FlowPortTests {
  private const double Atm = 101325;
  private const double AirConstant = 287;
  private const double Step = 0.2;

  #region Fixtures

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

  private static void Lay(TestWorld w, params int[] xs) {
    foreach (int x in xs)
      w.PlaceNode(Cell(x), "pipeflow", "we");
  }

  /// <summary>Gas in kg that fills 1 m3 of air at 20 C to <paramref name="atm"/> absolute.</summary>
  private static double AirAt(double atm) => atm * Atm / (AirConstant * 293.15);

  private static int Air(PipeFlowNetwork run, int x, double atm) =>
    run.Store.AddNode(Key(x), 1, 0, AirAt(atm), 20, AirConstant);

  private static FlowPort Port(TestWorld w, int x) => new(w.Networks, Key(x));

  /// <summary>Two runs of two cells, 0-1 and 3-4, the first pressurised and the second at the atmosphere, with a port on cell 1 coupled to cell 3.</summary>
  private static (
    TestWorld World,
    PipeFlowNetwork Main,
    PipeFlowNetwork Furnace
  ) Plant(out FlowPort feed, out FlowPort furnace, int far = 3) {
    var w = NewWorld();
    Lay(w, 0, 1, far, far + 1);
    var main = RunAt(w, 0);
    var inside = RunAt(w, far);
    main.Medium = FlowMedium.Air;
    inside.Medium = FlowMedium.Air;
    Air(main, 0, 3);
    Air(main, 1, 3);
    main.Store.AddEdge(0, 1, 0.05, 1, FlowPipeClass.Cast);
    Air(inside, far, 1);
    Air(inside, far + 1, 1);
    inside.Store.AddEdge(0, 1, 0.05, 1, FlowPipeClass.Cast);
    feed = Port(w, 1);
    furnace = Port(w, far);
    feed.CoupleTo(furnace, FlowGas.Air)!.SetArea(0.002);
    feed.SetSource(0.02, 20, FlowMedium.Air);
    furnace.SetHeatWatts(500);
    return (w, main, inside);
  }

  private static void Tick(TestWorld w, PipeFlowNetwork[] order, int steps = 1) {
    for (int i = 0; i < steps; i++) {
      foreach (var run in order)
        run.OnTick(w.Accessor, (float)Step, w.Networks);
      foreach (var run in order)
        run.OnTickPublished(w.Accessor, w.Networks);
    }
  }

  #endregion

  #region Couplings

  // Fails when a coupling reads the receiving run's live pressure, which depends on whether that run stepped first.
  [Fact]
  public void OrderIndependence_PermutedNetworks() {
    var first = Plant(out var feedA, out var furnaceA);
    var second = Plant(out var feedB, out var furnaceB);

    Tick(first.World, [first.Main, first.Furnace], 60);
    Tick(second.World, [second.Furnace, second.Main], 60);

    var a = feedA.Take()!.Value;
    var b = feedB.Take()!.Value;
    Assert.Equal(a.MassKg, b.MassKg, 12);
    Assert.Equal(a.PressureAtm, b.PressureAtm, 12);
    Assert.Equal(a.GasKg, b.GasKg, 12);
    var c = furnaceA.Take()!.Value;
    var d = furnaceB.Take()!.Value;
    Assert.Equal(c.PressureAtm, d.PressureAtm, 12);
    Assert.Equal(c.TemperatureC, d.TemperatureC, 12);
    Assert.Equal(c.GasKg, d.GasKg, 12);
    Assert.True(c.GasKg > 2 * AirAt(1) + 0.01, "the furnace received gas");
  }

  // Fails when the receiving run leaves the published mass in place after taking it, so every step delivers it again.
  [Fact]
  public void Coupling_StoresPlusInFlightConstant() {
    var (w, main, inside) = Plant(out var feed, out _);
    feed.SetSource(0, 20, FlowMedium.Air);
    double start = main.Store.TotalGasKg() + inside.Store.TotalGasKg();

    for (int i = 0; i < 40; i++) {
      Tick(w, [main, inside]);
      double inFlight = main.Couplings.Sum(c => c.PublishedKg);
      Assert.Equal(
        start,
        main.Store.TotalGasKg() + inside.Store.TotalGasKg() + inFlight,
        9
      );
    }

    Assert.True(inside.Store.TotalGasKg() > 2 * AirAt(1) + 0.01);
  }

  // Fails when a coupling books the draw of a sender whose chunk is unloaded, so the run publishes gas it never gave up.
  [Fact]
  public void Coupling_HeldSender_MakesNoGas() {
    var (w, main, inside) = Plant(out var feed, out _, 64);
    feed.SetSource(0, 20, FlowMedium.Air);
    Tick(w, [main, inside], 3);
    double before = Total(main, inside);

    w.UnloadChunkAt(Cell(1));
    Tick(w, [main, inside], 10);

    Assert.Equal(before, Total(main, inside), 9);
  }

  // Fails when the draw towards a receiver whose chunk is unloaded is still made, so the in-flight mass keeps growing.
  [Fact]
  public void Coupling_HeldReceiver_DrawsNothing() {
    var (w, main, inside) = Plant(out var feed, out _, 64);
    feed.SetSource(0, 20, FlowMedium.Air);
    Tick(w, [main, inside], 3);

    w.UnloadChunkAt(Cell(64));
    Tick(w, [main, inside], 2);
    double inFlight = main.Couplings.Sum(c => c.PublishedKg);
    Tick(w, [main, inside], 10);

    Assert.Equal(inFlight, main.Couplings.Sum(c => c.PublishedKg), 9);
  }

  // Fails when the receiver takes the in-flight mass while held, which its step then discards.
  [Fact]
  public void Coupling_HeldReceiver_DestroysNoGas() {
    var (w, main, inside) = Plant(out var feed, out _, 64);
    feed.SetSource(0, 20, FlowMedium.Air);
    Tick(w, [main, inside], 3);
    double before = Total(main, inside);

    w.UnloadChunkAt(Cell(64));
    Tick(w, [main, inside], 10);

    Assert.Equal(before, Total(main, inside), 9);
  }

  private static double Total(PipeFlowNetwork main, PipeFlowNetwork inside) =>
    main.Store.TotalGasKg()
    + inside.Store.TotalGasKg()
    + main.Couplings.Sum(c => c.PublishedKg + c.NextKg);

  // Fails when a coupling is found by its receiver alone, so a second port into the same node takes the first port's coupling and its area.
  [Fact]
  public void Coupling_TwoPortsIntoOneReceiver_StayApart() {
    var (w, main, _) = Plant(out _, out var furnace);

    Port(w, 0).CoupleTo(furnace, FlowGas.Air)!.SetArea(0.004);

    Assert.Equal(2, main.Couplings.Count);
    Assert.Equal(
      [0.002, 0.004],
      main.Couplings.Select(c => c.AreaM2).OrderBy(a => a)
    );
  }

  // Fails when the area setter is dropped, so a coupling closed by area 0 keeps passing gas.
  [Fact]
  public void Coupling_ClosedByAreaZero_PassesNothingMore() {
    var (w, main, inside) = Plant(out var feed, out var furnace);
    double start = inside.Store.TotalGasKg();
    Tick(w, [main, inside], 20);
    Assert.True(inside.Store.TotalGasKg() > start + 0.01);

    feed.CoupleTo(furnace, FlowGas.Air)!.SetArea(0);
    Tick(w, [main, inside], 3);
    double settled = inside.Store.TotalGasKg();
    Tick(w, [main, inside], 20);

    Assert.Equal(settled, inside.Store.TotalGasKg(), 12);
  }

  #endregion

  #region Declarations

  // Fails when a source of another medium is let into a run that already holds one.
  [Fact]
  public void Source_OfAnotherMedium_PassesNothing() {
    var w = NewWorld();
    Lay(w, 0, 1);
    var run = RunAt(w, 0);
    run.Medium = FlowMedium.Water;
    Air(run, 0, 1);
    Air(run, 1, 1);
    run.Store.AddEdge(0, 1, 0.05, 1, FlowPipeClass.Cast);
    double start = run.Store.TotalGasKg();

    Port(w, 0).SetSource(0.1, 20, FlowMedium.Air);
    Tick(w, [run], 10);

    Assert.Equal(start, run.Store.TotalGasKg(), 12);
    Assert.Equal(FlowMedium.Water, run.Medium);
  }

  // Fails when the first source into an ambient run does not give it its medium.
  [Fact]
  public void Source_IntoAnAmbientRun_ClaimsItsMedium() {
    var w = NewWorld();
    Lay(w, 0, 1);
    var run = RunAt(w, 0);
    Air(run, 0, 1);

    Port(w, 0).SetSource(0.1, 20, FlowMedium.Flue);
    Tick(w, [run], 5);

    Assert.Equal(FlowMedium.Flue, run.Medium);
    Assert.Equal(AirAt(1) + 0.1, run.Store.TotalGasKg(), 6);
  }

  // Fails when a fuel source leaves the run's grade at zero.
  [Fact]
  public void Source_OfFuel_SetsTheRunsGrade() {
    var w = NewWorld();
    Lay(w, 0);
    var run = RunAt(w, 0);
    run.Store.AddNode(Key(0), 1, 0, 0, 20, AirConstant);

    Port(w, 0).SetSource(0.1, 20, FlowMedium.Fuel, 0.5);
    Tick(w, [run]);

    Assert.Equal(FlowMedium.Fuel, run.Medium);
    Assert.Equal(0.5, run.Grade, 12);
  }

  // Fails when declared heat is not applied to the node's content.
  [Fact]
  public void HeatWatts_RaiseTheContentsTemperature() {
    var w = NewWorld();
    Lay(w, 0);
    var run = RunAt(w, 0);
    int node = Air(run, 0, 1);
    double kg = AirAt(1);

    Port(w, 0).SetHeatWatts(1000);
    Tick(w, [run], 10);

    double expected = 20 + 1000 * 10 * Step / (kg * 3.5 * AirConstant);
    Assert.Equal(expected, run.Store.TemperatureC(node), 1);
  }

  // Fails when a read leaves the accrued mass in place, so the next read repeats it.
  [Fact]
  public void Take_ReadsWhatAccruedThenStartsOver() {
    var w = NewWorld();
    Lay(w, 0);
    var run = RunAt(w, 0);
    run.Medium = FlowMedium.Air;
    Air(run, 0, 1);
    var port = Port(w, 0);

    port.SetSource(0.1, 20, FlowMedium.Air);
    Tick(w, [run], 5);
    var first = port.Take()!.Value;
    port.SetSource(0, 20, FlowMedium.Air);
    Tick(w, [run], 5);
    var second = port.Take()!.Value;

    Assert.Equal(0.1, first.MassKg, 6);
    Assert.True(first.EnthalpyJ > 0);
    Assert.Equal(0, second.MassKg, 12);
    Assert.True(second.PressureAtm > first.PressureAtm * 0.99);
  }

  // Fails when Take answers a port that no machine declared at.
  [Fact]
  public void Take_OfAPortNoMachineDeclaredAt_IsNull() {
    var w = NewWorld();
    Lay(w, 0);
    Air(RunAt(w, 0), 0, 1);

    Assert.Null(Port(w, 0).Take());
  }

  #endregion

  #region Links

  private static (
    TestWorld World,
    PipeFlowNetwork Run,
    FlowPort From,
    FlowPort To
  ) Pair(double fromAtm, double toAtm) {
    var w = NewWorld();
    Lay(w, 0, 1);
    var run = RunAt(w, 0);
    run.Medium = FlowMedium.Air;
    Air(run, 0, fromAtm);
    Air(run, 1, toAtm);
    run.Store.AddEdge(0, 1, 0.05, 1, FlowPipeClass.Cast, 0);
    return (w, run, Port(w, 0), Port(w, 1));
  }

  // Fails when the swept volume is ignored, so a closed link moves nothing.
  [Fact]
  public void Link_SweptVolume_CarriesUpstreamContent() {
    var (w, run, from, to) = Pair(1, 1);
    Assert.True(from.LinkTo(to)!.SetSweptVolume(0.01));

    Tick(w, [run], 5);

    Assert.Equal(0.01 * AirAt(1) * 5 * Step, run.Store.GasKg(1) - AirAt(1), 3);
  }

  // Fails when the pump curve ignores its stall lift, so a pump pushes against any pressure.
  [Fact]
  public void Link_PumpCurve_StopsAtItsStallLift() {
    var (w, run, from, to) = Pair(1, 1.5);
    Assert.True(from.LinkTo(to)!.SetPumpCurve(0.01, 0.4 * Atm));
    double start = run.Store.GasKg(1);

    Tick(w, [run], 5);

    Assert.Equal(start, run.Store.GasKg(1), 9);
  }

  // Fails when the pump delivers nothing against a lift below its stall.
  [Fact]
  public void Link_PumpCurve_DeliversBelowItsStallLift() {
    var (w, run, from, to) = Pair(1, 1.1);
    Assert.True(from.LinkTo(to)!.SetPumpCurve(0.01, 0.4 * Atm));
    double start = run.Store.GasKg(1);

    Tick(w, [run], 5);

    Assert.True(run.Store.GasKg(1) > start + 0.005);
  }

  // Fails when a link laid from the other port is driven from this one, so a pump runs backwards.
  [Fact]
  public void Link_LaidTheOtherWay_RefusesPumpDeclarations() {
    var (_, _, from, to) = Pair(1, 1);

    Assert.False(to.LinkTo(from)!.SetSweptVolume(0.01));
    Assert.True(from.LinkTo(to)!.SetShare(1));
  }

  // Fails when a link is answered for two ports in different runs.
  [Fact]
  public void LinkTo_AnotherRun_IsNull() {
    _ = Plant(out var feed, out var furnace);

    Assert.Null(feed.LinkTo(furnace));
  }

  #endregion

  #region Runs merging and splitting

  // Fails when a merge drops the absorbed run's port declarations.
  [Fact]
  public void Merge_CarriesPortDeclarations() {
    var w = NewWorld();
    Lay(w, 0, 1, 3, 4);
    var far = RunAt(w, 3);
    far.Medium = FlowMedium.Air;
    Air(far, 3, 1);
    Air(far, 4, 1);
    Port(w, 4).SetSource(0.05, 20, FlowMedium.Air);
    Port(w, 1).SetHeatWatts(10);

    Lay(w, 2);

    var merged = RunAt(w, 0);
    Assert.Same(merged, RunAt(w, 4));
    Assert.True(merged.Ports.ContainsKey(Key(4)));
    Assert.True(merged.Ports.ContainsKey(Key(1)));
  }

  // Fails when an ambient run merging with a fuel run takes the fuel grade undiluted, so air mixes in as rich gas.
  [Fact]
  public void Merge_FuelWithAmbient_DilutesTheGrade() {
    var w = NewWorld();
    Lay(w, 0, 1, 3, 4);
    var fuel = RunAt(w, 0);
    fuel.Medium = FlowMedium.Fuel;
    Air(fuel, 0, 1);
    Air(fuel, 1, 1);
    Port(w, 1).SetSource(0.02, 20, FlowMedium.Fuel, 0.8);
    Tick(w, [fuel], 1);
    double grade = fuel.Grade;
    Assert.True(grade > 0);
    var ambient = RunAt(w, 3);
    Air(ambient, 3, 1);
    Air(ambient, 4, 1);
    double fuelGas = fuel.Store.TotalGasKg();
    double total = fuelGas + ambient.Store.TotalGasKg();

    Lay(w, 2);

    var merged = RunAt(w, 0);
    Assert.Same(merged, RunAt(w, 4));
    Assert.Equal(grade * fuelGas / total, merged.Grade, 9);
  }

  // Fails when a split leaves the fragment's port declarations with the run it left.
  [Fact]
  public void Split_MovesPortDeclarationsToTheirFragment() {
    var w = NewWorld();
    Lay(w, 0, 1, 2, 3, 4);
    var run = RunAt(w, 0);
    run.Medium = FlowMedium.Air;
    for (int x = 0; x <= 4; x++)
      Air(run, x, 1);
    Port(w, 4).SetSource(0.05, 20, FlowMedium.Air);
    Port(w, 0).SetHeatWatts(10);

    w.RemoveNode(Cell(2));

    Assert.True(RunAt(w, 4).Ports.ContainsKey(Key(4)));
    Assert.False(RunAt(w, 0).Ports.ContainsKey(Key(4)));
    Assert.True(RunAt(w, 0).Ports.ContainsKey(Key(0)));
  }

  private sealed class Machine : BlockEntity;

  // Fails when At hands a client a port, though the run it would join lives on the server.
  [Theory]
  [InlineData(EnumAppSide.Client, false)]
  [InlineData(EnumAppSide.Server, true)]
  public void At_IsNullOnAClient(EnumAppSide side, bool handed) {
    var w = NewWorld();
    var api = Substitute.For<ICoreAPI>();
    api.Side.Returns(side);
    api.ModLoader.GetModSystem<BlockNetworkModSystem>().Returns(w.Networks);
    var machine = new Machine { Pos = Cell(0), Api = api };

    Assert.Equal(handed, FlowPort.At(machine, 0) != null);
  }

  #endregion

  #region Lumped beside physical

  // Fails when a network type's members are resolved without regard to the type, so a lumped cell joins the flow run beside it.
  [Fact]
  public void LumpedBesidePhysical_Refuses() {
    var w = NewWorld();
    w.PlaceNode(Cell(0), "pipe", "we");
    w.PlaceNode(Cell(1), "pipeflow", "we");
    w.PlaceNode(Cell(2), "pipe", "we");

    var lumped = w.NetworkAt(Cell(0));
    var flow = w.NetworkAt(Cell(1));
    Assert.IsType<PipeNetwork>(lumped);
    Assert.IsType<PipeFlowNetwork>(flow);
    Assert.NotSame(lumped, w.NetworkAt(Cell(2)));
    Assert.Single(flow!.Nodes);
    Assert.Single(lumped!.Nodes);
    Assert.Equal(3, w.Networks.AllNetworks.Count());
  }

  #endregion
}
