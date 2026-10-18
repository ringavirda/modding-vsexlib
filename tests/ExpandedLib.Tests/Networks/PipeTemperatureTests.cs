using System;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers content and wall temperature of the physical pipe network: advection, mixing, wall exchange and
/// the ambient, in <see cref="PipeFlowStore"/>.
/// </summary>
public class PipeTemperatureTests {
  #region Fixtures

  private const double Volume = 0.02;
  private const double Bore = 0.05;
  private const double GasConstant = 461.5;
  private const double AtmosphereKg = 101325 * Volume / (GasConstant * 293.15);

  private static NodeKey Cell(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private static int Add(
    PipeFlowStore store,
    int index,
    double gasKg,
    double temperatureC
  ) => store.AddNode(Cell(index), Volume, 0, gasKg, temperatureC);

  private static PipeWall Wall(
    double outerAreaM2 = 0.3,
    double capacity = 2000,
    bool lagged = false
  ) => new(capacity, 0.3, outerAreaM2, lagged);

  #endregion

  #region Advection

  // Fails when a flow carries the downwind node's temperature instead of the upwind node's, so the hot node heats as it empties and the cold one mixes the wrong gas.
  [Fact]
  public void Advection_CarriesTheUpwindTemperature() {
    var store = new PipeFlowStore();
    int hot = Add(store, 0, AtmosphereKg * 2, 100);
    int cold = Add(store, 1, AtmosphereKg, 20);
    store.AddEdge(hot, cold, Bore, 1, FlowPipeClass.Cast);

    store.Step(0.2);

    double moved = store.FlowKgPerSecond(0) * 0.2;
    Assert.True(moved > 0);
    Assert.Equal(100, store.TemperatureC(hot), 6);
    double mixed =
      (AtmosphereKg * 293.15 + moved * 373.15) / (AtmosphereKg + moved)
      - 273.15;
    Assert.Equal(mixed, store.TemperatureC(cold), 6);
  }

  // Fails when a source ignores its declared temperature and delivers at the node's own.
  [Fact]
  public void Source_DeliversAtItsDeclaredTemperature() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, 0.001, 20);
    store.SetSource(node, 0.05, 300);

    for (int i = 0; i < 100; i++)
      store.Step(0.2);

    Assert.InRange(store.TemperatureC(node), 295, 300.001);
  }

  // Fails when gas entering through an opening arrives at the node's temperature instead of the ambient.
  [Fact]
  public void Opening_DrawsAirInAtAmbient() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg * 0.2, 80);
    store.AddOpening(node, 1e-4, 101325, FlowGas.Air);

    for (int i = 0; i < 100; i++)
      store.Step(0.2);

    Assert.True(store.GasKg(node) > AtmosphereKg * 0.5);
    Assert.InRange(store.TemperatureC(node), 20, 40);
  }

  // Fails when gas leaving through an opening takes the ambient temperature off the node's energy, so a venting hot node cools.
  [Fact]
  public void Opening_VentsAtTheNodeTemperature() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg * 3, 150);
    store.AddOpening(node, 1e-4, 101325, FlowGas.Steam);

    for (int i = 0; i < 20; i++)
      store.Step(0.2);

    Assert.True(store.OpeningFlowKgPerSecond(0) > 0);
    Assert.Equal(150, store.TemperatureC(node), 6);
  }

  #endregion

  #region Walls

  // Fails when a port reads the run mean or the downwind node, or advection carries the downwind temperature: the consumer node leaves the analytic profile of heat lost along the line.
  [Fact]
  public void PortReadsArrivingTemperature() {
    const int nodes = 20;
    const double flow = 0.05;
    var store = new PipeFlowStore();
    for (int i = 0; i < nodes; i++)
      Add(store, i, AtmosphereKg, 20);
    for (int i = 1; i < nodes; i++)
      store.AddEdge(i - 1, i, Bore, 1, FlowPipeClass.Cast);
    var wall = Wall();
    for (int i = 0; i < nodes; i++)
      store.SetWall(i, wall, 20);
    int leg = Add(store, 100, AtmosphereKg, 20);
    store.AddNode(Cell(101), Volume, 0, AtmosphereKg, 20);
    store.AddEdge(5, leg, Bore, 1, FlowPipeClass.Cast);
    store.AddEdge(leg, leg + 1, Bore, 1, FlowPipeClass.Cast);
    store.SetWall(leg, wall, 20);
    store.SetWall(leg + 1, wall, 20);
    store.SetSource(0, flow, 300);
    store.SetSource(nodes - 1, -flow);

    for (int i = 0; i < 6000; i++)
      store.Step(0.2);

    double carried = flow * 3.5 * GasConstant;
    double through =
      wall.InnerUaWPerK
      * wall.OuterUaWPerK
      / (wall.InnerUaWPerK + wall.OuterUaWPerK);
    double expected = 300;
    for (int i = 0; i < nodes; i++)
      expected = (carried * expected + through * 20) / (carried + through);
    Assert.InRange(expected, 100, 290);
    Assert.Equal(expected, store.TemperatureC(nodes - 1), 1.5);
    Assert.InRange(store.TemperatureC(leg), 19, 22);
    Assert.InRange(store.TemperatureC(leg + 1), 19, 22);
  }

  // Fails when the ambient term is dropped, or the node's ambient is ignored for a fixed 20 C: the dead node does not settle at its own ambient.
  [Fact]
  public void DeadNode_CoolsToItsAmbient() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg, 150);
    store.SetWall(node, Wall(), 150);
    store.SetAmbient(node, 5);

    for (int i = 0; i < 20000; i++)
      store.Step(1);

    Assert.True(store.Asleep);
    Assert.InRange(store.TemperatureC(node), 5, 6.001);
    Assert.InRange(store.WallTemperatureC(node), 5, 6.001);
  }

  // Fails when a hot walled run stops cooling once it rests.
  [Fact]
  public void HotRun_KeepsCoolingWhileItRests() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg, 150);
    store.SetWall(node, Wall(capacity: 1e6), 150);

    for (int i = 0; i < 50; i++) {
      store.Step(0.2);
    }
    Assert.True(store.TemperatureC(node) < 150);
    Assert.True(store.WallTemperatureC(node) < 150);
  }

  // Fails when lagging does not cut the outside coefficient to a fifth, so the lagged wall loses as much heat as the bare one.
  [Fact]
  public void Lagging_CutsLossToAFifth() {
    double Loss(bool lagged) {
      var store = new PipeFlowStore();
      int node = Add(store, 0, AtmosphereKg, 100);
      store.SetWall(node, Wall(capacity: 1e6, lagged: lagged), 100);
      store.Step(0.2);
      return 100 - store.WallTemperatureC(node);
    }

    double bare = Loss(false);
    Assert.True(bare > 0);
    Assert.Equal(0.2, Loss(true) / bare, 3);
  }

  // Fails when the wall gives content heat only to the ambient: a hot wall does not warm the cold gas inside it.
  [Fact]
  public void Wall_WarmsTheContent() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg, 20);
    store.SetWall(node, Wall(capacity: 1e6), 120);
    store.SetAmbient(node, 120);

    for (int i = 0; i < 300; i++)
      store.Step(1);

    Assert.InRange(store.TemperatureC(node), 118, 120.001);
  }

  // Fails when a frozen node still exchanges heat with its wall and the ambient.
  [Fact]
  public void FrozenNode_KeepsItsTemperatures() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg, 150);
    store.SetWall(node, Wall(), 150);
    store.SetFrozen(node, true);

    for (int i = 0; i < 20; i++)
      store.Step(1);

    Assert.Equal(150, store.TemperatureC(node));
    Assert.Equal(150, store.WallTemperatureC(node));
  }

  // Fails when a split or merge drops the wall, so the copied node stops cooling.
  [Fact]
  public void Take_CarriesTheWall() {
    var source = new PipeFlowStore();
    int node = Add(source, 0, AtmosphereKg, 90);
    source.SetWall(node, Wall(), 90);
    source.SetAmbient(node, 5);
    var copy = new PipeFlowStore();

    copy.Take(
      source,
      new System.Collections.Generic.HashSet<NodeKey> { Cell(0) }
    );
    copy.Step(1);

    int taken = copy.IndexOf(Cell(0));
    Assert.True(copy.WallTemperatureC(taken) < 90);
    Assert.True(copy.TemperatureC(taken) < 90);
  }

  #endregion

  #region Rest

  // Fails when a sleeping hot walled run gets no thermal tick, so its temperature stands still, or when the tick solves flow and counts a step.
  [Fact]
  public void Rest_HotRunCoolsAtOneHertzWithoutSolving() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg, 60);
    store.SetWall(node, Wall(outerAreaM2: 0.01), 60);
    store.Step(1e-6);
    Assert.True(store.Asleep);

    int solved = store.StepsSolved;
    double before = store.TemperatureC(node);
    store.Step(0.4);
    Assert.Equal(before, store.TemperatureC(node));
    for (int i = 0; i < 20; i++)
      store.Step(0.4);

    Assert.True(store.Asleep);
    Assert.Equal(solved, store.StepsSolved);
    Assert.True(store.TemperatureC(node) < before);
    Assert.True(store.WallTemperatureC(node) < 60);
  }

  // Fails when a thermal tick that moves a node's pressure by more than 50 Pa leaves the run asleep.
  [Fact]
  public void Rest_TickMovingPressureWakesTheRun() {
    var store = new PipeFlowStore();
    int node = Add(store, 0, AtmosphereKg, 60);
    store.SetWall(node, Wall(capacity: 10), 60);
    store.Step(1e-6);
    Assert.True(store.Asleep);

    store.Step(1);

    Assert.False(store.Asleep);
    int solved = store.StepsSolved;
    store.Step(0.2);
    Assert.Equal(solved + 1, store.StepsSolved);
  }

  // Fails when a thermal tick that condenses steam, moving the pressure by less than 50 Pa, leaves the run asleep.
  [Fact]
  public void Rest_TickChangingPhaseWakesTheRun() {
    var store = new PipeFlowStore();
    int node = store.AddNode(
      Cell(0),
      Volume,
      0,
      4e-4,
      25,
      GasConstant,
      0,
      0,
      true
    );
    store.SetWall(node, Wall(outerAreaM2: 0.01), 25);
    store.Step(1e-6);
    Assert.True(store.Asleep);
    Assert.Equal(0, store.LiquidKg(node));

    double pressure = store.PressurePa(node);
    for (int i = 0; i < 40000 && store.Asleep; i++) {
      store.Step(1);
      Assert.InRange(Math.Abs(store.PressurePa(node) - pressure), 0, 50);
      pressure = store.PressurePa(node);
    }

    Assert.False(store.Asleep);
    Assert.True(store.LiquidKg(node) > 0);
  }

  #endregion

  #region Save

  // Fails when SaveNode writes the content temperature into w, or RestoreNode drops the saved wall temperature.
  [Fact]
  public void Reload_KeepsTheWallTemperature() {
    var w = new TestWorld();
    var live = new PipeFlowNetwork(w.Networks);
    int node = Add(live.Store, 0, AtmosphereKg, 120);
    var wall = Wall();
    live.Store.SetWall(node, wall, 80);
    var host = new TreeAttribute();
    live.SaveNode(Cell(0), host);

    var rebuilt = new PipeFlowNetwork(w.Networks);
    Assert.True(rebuilt.RestoreNode(Cell(0), host, Volume, 0, wall: wall));

    int restored = rebuilt.Store.IndexOf(Cell(0));
    Assert.Equal(80, rebuilt.Store.WallTemperatureC(restored));
    Assert.Equal(120, rebuilt.Store.TemperatureC(restored));
  }

  #endregion
}
