using System;

namespace ExpandedLib.Industry.Heat;

/// <summary>Lumped heat flow of a body of fixed heat capacity: exact approach to a target, the fire's conductance, fuel burn and a hot stack's cooling speed.</summary>
public static class HeatFlow {
  /// <summary>The temperature after a body of heat capacity <paramref name="c"/> is held against a constant target through conductance <paramref name="g"/>.</summary>
  /// <param name="t">Starting temperature (C).</param>
  /// <param name="target">Temperature the body settles at (C).</param>
  /// <param name="c">Heat capacity (kJ/K); 0 or less returns <paramref name="t"/>.</param>
  /// <param name="g">Total conductance to the target (kJ/(K game h)); 0 or less returns <paramref name="t"/>.</param>
  /// <param name="hours">Elapsed game hours; 0 or less returns <paramref name="t"/>.</param>
  /// <returns><c>target + (t - target) * exp(-g * hours / c)</c>, the exact solution of <c>c dT/dt = g (target - t)</c>, so any slicing of <paramref name="hours"/> lands on the same temperature.</returns>
  public static double Approach(
    double t,
    double target,
    double c,
    double g,
    double hours
  ) {
    if (c <= 0d || g <= 0d || hours <= 0d)
      return t;
    return target + (t - target) * Math.Exp(-g * hours / c);
  }

  /// <summary>The flame's conductance into the lining: the fire's power over the span the flame stands above ambient at the settled temperature.</summary>
  /// <param name="fireKw">Fire power (kW); 0 or less returns 0.</param>
  /// <param name="settled">Temperature the chamber settles at under that fire (C).</param>
  /// <param name="ambient">Ambient temperature (C).</param>
  /// <returns><c>fireKw * 3600 / max(1, settled - ambient)</c> in kJ/(K game h); the span is floored at 1 K.</returns>
  public static double FireConductance(
    double fireKw,
    double settled,
    double ambient
  ) => fireKw <= 0d ? 0d : fireKw * 3600d / Math.Max(1d, settled - ambient);

  /// <summary>Fuel units a fire burns over a span of game hours.</summary>
  /// <param name="fireKw">Fire power (kW); 0 or less returns 0.</param>
  /// <param name="hours">Elapsed game hours; 0 or less returns 0.</param>
  /// <param name="burnSeconds">Burn duration of one unit (s); 0 or less returns 0.</param>
  /// <param name="kjPerBurnSecond">Heat the fuel gives per burn second (kJ/s); 0 or less returns 0.</param>
  /// <returns><c>fireKw * 3600 * hours / (burnSeconds * kjPerBurnSecond)</c> units.</returns>
  public static double BurnUnits(
    double fireKw,
    double hours,
    double burnSeconds,
    double kjPerBurnSecond
  ) =>
    fireKw <= 0d || hours <= 0d || burnSeconds <= 0d || kjPerBurnSecond <= 0d
      ? 0d
      : fireKw * 3600d * hours / (burnSeconds * kjPerBurnSecond);

  /// <summary>The vanilla cooldown speed of a stack of hot units.</summary>
  /// <param name="speedAt100">Cooldown speed of a stack of 100 units (C per game hour).</param>
  /// <param name="units">Units held; 0 or less returns <paramref name="speedAt100"/>.</param>
  /// <returns><c>speedAt100 * (100 / units)^(1/3)</c>.</returns>
  public static double CoolingSpeed(double speedAt100, double units) =>
    units <= 0d ? speedAt100 : speedAt100 * Math.Cbrt(100d / units);
}
