namespace ExpandedLib.Industry.Pipes;

/// <summary>What a port's node did between two <see cref="FlowPort.Take"/> calls.</summary>
/// <param name="MassKg">Net mass in kg the node gained through its source and couplings; negative when it was drawn down.</param>
/// <param name="EnthalpyJ">Heat in J that mass carried, from 0 C, plus the heat declared into the node.</param>
/// <param name="PressureAtm">Mean absolute pressure over the steps, in atm.</param>
/// <param name="TemperatureC">Mean content temperature over the steps, in C.</param>
/// <param name="GasKg">Gas the node holds now, in kg.</param>
/// <param name="AirKg">Air the node holds beside its gas now, in kg.</param>
/// <param name="LiquidKg">Liquid the node holds now, in kg.</param>
public readonly record struct PortReading(
  double MassKg,
  double EnthalpyJ,
  double PressureAtm,
  double TemperatureC,
  double GasKg,
  double AirKg,
  double LiquidKg
);
