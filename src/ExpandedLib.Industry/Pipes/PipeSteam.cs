using System;

namespace ExpandedLib.Industry.Pipes;

/// <summary>Saturation properties of water used by the two-phase nodes of <see cref="PipeFlowStore"/>.</summary>
internal static class PipeSteam {
  private const double CriticalC = 373.946;
  private const double CriticalK = 647.096;
  private const double CriticalPa = 22.064e6;
  private const double KelvinOffset = 273.15;
  private const double SlopeStepC = 0.25;

  /// <summary>Latent heat of vaporisation at 0 C in J/kg, the enthalpy steam holds above liquid water of the same temperature once its sensible heat is counted at the gas heat capacity.</summary>
  public const double LatentZeroJPerKg = 2.501e6;

  /// <summary>Saturation pressure of water in Pa at <paramref name="temperatureC"/> (Wagner and Pruss): 101 kPa at 100 C, 5.0 atm at 152 C. Temperatures from the critical point up are taken as the critical point's less 0.1 C.</summary>
  /// <param name="temperatureC">Temperature in C.</param>
  public static double SaturationPressurePa(double temperatureC) {
    double t = Math.Min(temperatureC, CriticalC - 0.1) + KelvinOffset;
    double tau = 1 - t / CriticalK;
    double root = Math.Sqrt(tau);
    double tau3 = tau * tau * tau;
    double sum =
      -7.85951783 * tau
      + 1.84408259 * tau * root
      - 11.7866497 * tau3
      + 22.6807411 * tau3 * root
      - 15.9618719 * tau3 * tau
      + 1.80122502 * tau3 * tau3 * tau * root;
    return CriticalPa * Math.Exp(CriticalK / t * sum);
  }

  /// <summary>Slope of the saturation pressure in Pa/K at <paramref name="temperatureC"/>.</summary>
  /// <param name="temperatureC">Temperature in C.</param>
  public static double SaturationSlopePaPerK(double temperatureC) =>
    (
      SaturationPressurePa(temperatureC + SlopeStepC)
      - SaturationPressurePa(temperatureC - SlopeStepC)
    ) / (2 * SlopeStepC);
}
