using ExpandedLib.Networks;

namespace ExpandedLib.Industry.Pipes;

/// <summary>A machine's declarations at one port node of a run and the readings accrued there since the last <see cref="FlowPort.Take"/>.</summary>
internal sealed class PortState {
  private const double AtmPa = 101325;

  public double HeatWatts { get; set; }

  public double SourceKgPerSecond { get; set; }

  public double SourceTemperatureC { get; set; } = double.NaN;

  public FlowMedium SourceMedium { get; set; }

  public double SourceGrade { get; set; }

  /// <summary>True when the source was declared since the store last received it.</summary>
  public bool SourceDirty { get; set; }

  private double _massKg;
  private double _enthalpyJ;
  private double _pressureSumPa;
  private double _temperatureSumC;
  private int _steps;

  /// <summary>Adds the step the store just made at <paramref name="node"/>, which lasted <paramref name="dt"/> seconds.</summary>
  public void Accrue(PipeFlowStore store, int node, double dt) {
    double applied = store.AppliedSourceKg(node);
    double temperatureC = store.TemperatureC(node);
    _massKg += applied;
    _enthalpyJ +=
      applied * store.GasHeatCapacity(node) * temperatureC
      + store.HeatWatts(node) * dt;
    _pressureSumPa += store.PressurePa(node);
    _temperatureSumC += temperatureC;
    _steps++;
  }

  /// <summary>Returns what accrued since the last call and the node's stores now, then starts over.</summary>
  public PortReading Take(PipeFlowStore store, int node) {
    PortReading reading = new(
      _massKg,
      _enthalpyJ,
      (_steps > 0 ? _pressureSumPa / _steps : store.PressurePa(node)) / AtmPa,
      _steps > 0 ? _temperatureSumC / _steps : store.TemperatureC(node),
      store.GasKg(node),
      store.AirKg(node),
      store.LiquidKg(node)
    );
    _massKg = 0;
    _enthalpyJ = 0;
    _pressureSumPa = 0;
    _temperatureSumC = 0;
    _steps = 0;
    return reading;
  }
}
