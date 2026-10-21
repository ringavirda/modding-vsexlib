using System;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers two-phase steam in the physical pipe network: the saturation curve, the flash of a water node to
/// its saturation pressure, condensate on a cold wall and a boiler blowing down, in <see cref="PipeFlowStore"/>.
/// </summary>
public class PipeSteamTests {
  #region Fixtures

  private const double Atmosphere = 101325;
  private const double SteamConstant = 461.5;
  private const double SteamHeat = 3.5 * SteamConstant;
  private const double LiquidHeat = 4186;

  private static NodeKey Cell(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private static double LatentHeat(double temperatureC) =>
    PipeSteam.LatentZeroJPerKg + (SteamHeat - LiquidHeat) * temperatureC;

  #endregion

  #region Saturation

  // Fails when the saturation curve is off its anchors, so a boiler's pressure at a temperature is not the table's.
  [Theory]
  [InlineData(100, 1)]
  [InlineData(152, 5)]
  [InlineData(180, 10)]
  public void Saturation_FollowsTheSteamTable(double temperatureC, double atm) {
    double pressure = PipeSteam.SaturationPressurePa(temperatureC);

    Assert.InRange(pressure / (atm * Atmosphere), 0.98, 1.02);
  }

  #endregion

  #region Condensate

  // Fails when the wall's heat is left out of the phase balance or the condensing coefficient falls to the gas one, so a cold run condenses far less steam than its wall holds heat.
  [Fact]
  public void Warmup_CondensesWallHeat() {
    const int nodes = 4;
    const double capacity = 50000;
    var store = new PipeFlowStore();
    for (int i = 0; i < nodes; i++) {
      int node = store.AddNode(
        Cell(i),
        0.1,
        0,
        0.0005,
        20,
        SteamConstant,
        0,
        0,
        true
      );
      store.SetWall(node, new PipeWall(capacity, 0.6, 0), 20);
      if (i > 0)
        store.AddEdge(i - 1, i, 0.05, 3, FlowPipeClass.Cast);
    }
    store.AddOpening(nodes - 1, 1e-3, Atmosphere, FlowGas.Steam);
    store.SetSource(0, 0.05, 100);

    for (int i = 0; i < 3000; i++)
      store.Step(0.2);

    double heat = 0;
    for (int i = 0; i < nodes; i++)
      heat += capacity * (store.WallTemperatureC(i) - 20);
    double expected = heat / LatentHeat(100);
    Assert.InRange(store.WallTemperatureC(nodes - 1), 99, 102);
    Assert.InRange(store.TotalLiquidKg(), expected * 0.95, expected * 1.05);
  }

  // Fails when a liquid-full node's wall exchanges at the gas coefficient, so a water-filled run warms its wall a fifteenth as fast.
  [Fact]
  public void Water_FullNodeExchangesAtTheWaterCoefficient() {
    const double capacity = 10000;
    var store = new PipeFlowStore();
    int node = store.AddNode(
      Cell(0),
      0.02,
      0,
      0,
      90,
      SteamConstant,
      0.995 * 1000 * 0.02,
      0,
      true
    );
    store.SetWall(node, new PipeWall(capacity, 1, 0), 20);

    for (int i = 0; i < 600; i++)
      store.Step(0.2);

    double content = store.LiquidKg(node) * LiquidHeat;
    double settled = (capacity * 20 + content * 90) / (capacity + content);
    Assert.InRange(store.WallTemperatureC(node), settled - 2, settled + 2);
  }

  #endregion

  #region Blow-down

  // Fails when a water node does not flash, so a boiler behind an open end loses its pressure to the atmosphere in seconds instead of holding saturation while its water boils off.
  [Theory]
  [InlineData(0.2)]
  [InlineData(1)]
  public void OpenEnd_BlowsBoilerDown(double dt) {
    const double water = 600;
    var store = new PipeFlowStore();
    double start = 152;
    int boiler = store.AddNode(
      Cell(0),
      1,
      0,
      PipeSteam.SaturationPressurePa(start)
        * (1 - water / 1000)
        / (SteamConstant * (start + 273.15)),
      start,
      SteamConstant,
      water,
      0,
      true
    );
    int previous = boiler;
    for (int i = 1; i <= 10; i++) {
      int node = store.AddNode(
        Cell(i),
        0.0059,
        0,
        0.0059 * Atmosphere / (SteamConstant * (start + 273.15)),
        start,
        SteamConstant,
        0,
        0,
        true
      );
      store.AddEdge(previous, node, 0.05, 3, FlowPipeClass.Cast);
      previous = node;
    }
    store.AddOpening(
      previous,
      Math.PI * 0.05 * 0.05 / 4,
      Atmosphere,
      FlowGas.Steam
    );

    for (int i = 0; i < 100 / dt; i++)
      store.Step(dt);

    Assert.InRange(store.PressurePa(boiler) / Atmosphere, 1.8, 2.6);
    Assert.InRange(store.TemperatureC(boiler), 115, 130);

    for (int i = 0; i < 500 / dt; i++)
      store.Step(dt);

    double end = store.TemperatureC(boiler);
    Assert.InRange(store.PressurePa(boiler) / Atmosphere, 1, 1.05);
    Assert.InRange(end, 99, 101);
    double boiled = water - store.LiquidKg(boiler);
    double expected = water * LiquidHeat * (start - end) / LatentHeat(126);
    Assert.InRange(boiled, expected * 0.9, expected * 1.1);
  }

  #endregion

  #region Hammer

  // Fails when a non-cast class takes the crack, or the 10 m/s, 25 % liquid or cold-wall condition is dropped, so a plated run cracks, or a dry or warm node bangs.
  [Theory]
  [InlineData(0, true, 0.3, 20, true)]
  [InlineData(1, false, 0.3, 20, true)]
  [InlineData(2, false, 0.3, 20, true)]
  [InlineData(3, false, 0.3, 20, true)]
  [InlineData(0, false, 0.1, 20, false)]
  [InlineData(0, false, 0.3, 200, false)]
  public void Hammer_CracksCastOnly(
    int pipeClass,
    bool cracked,
    double fill,
    double wallC,
    bool banged
  ) {
    var store = new PipeFlowStore();
    int header = store.AddNode(
      Cell(0),
      1,
      0,
      3 * Atmosphere / (SteamConstant * 425.15),
      152,
      SteamConstant,
      0,
      0,
      true
    );
    int pot = store.AddNode(
      Cell(1),
      0.1,
      0,
      0.0005,
      100,
      SteamConstant,
      fill * 0.1 * 1000,
      0,
      true
    );
    store.SetWall(pot, new PipeWall(50000, 0.6, 0), wallC);
    int edge = store.AddEdge(header, pot, 0.05, 1, (FlowPipeClass)pipeClass);
    store.AddOpening(pot, 1e-3, Atmosphere, FlowGas.Steam);

    for (int i = 0; i < 50; i++)
      store.Step(0.05);

    Assert.Equal(cracked, store.IsCracked(edge));
    Assert.Equal(banged, store.HammerBangs(edge) > 0);
  }

  #endregion
}
