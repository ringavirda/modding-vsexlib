using System;

namespace ExpandedLib.Industry.Pipes;

/// <summary>Mass flow of gas through a sharp orifice between two fixed pressures: Napier's rule when choked, the isentropic nozzle form below.</summary>
internal static class GasOrifice {
  /// <summary>Upstream-to-downstream pressure ratio from which the flow is choked.</summary>
  public const double ChokeRatio = 1.85;

  /// <summary>Temperature in K at which the air coefficient is quoted.</summary>
  public const double AmbientK = 293;

  /// <summary>Gas constant of air in J/(kg K).</summary>
  public const double AirConstant = 287;

  private const double Discharge = 0.97;

  /// <summary>Choked mass flow per m2 of area and Pa of upstream pressure, in kg/(s m2 Pa): 1.457e-3 for steam (Napier), 2.3e-3 sqrt(293 / T) for air.</summary>
  /// <param name="gas">The gas passed.</param>
  /// <param name="upstreamK">Upstream temperature in K, above zero.</param>
  public static double ChokedCoefficient(FlowGas gas, double upstreamK) =>
    gas == FlowGas.Steam ? 1.457e-3 : 2.3e-3 * Math.Sqrt(AmbientK / upstreamK);

  /// <summary>True when the flow from <paramref name="upstreamPa"/> to <paramref name="downstreamPa"/> is choked.</summary>
  public static bool IsChoked(double upstreamPa, double downstreamPa) =>
    upstreamPa >= ChokeRatio * downstreamPa;

  /// <summary>Mass flow in kg/s from the upstream side to the downstream side.</summary>
  /// <param name="upstreamPa">Absolute upstream pressure in Pa; zero or less passes nothing.</param>
  /// <param name="downstreamPa">Absolute downstream pressure in Pa, from zero to <paramref name="upstreamPa"/>.</param>
  /// <param name="upstreamK">Upstream temperature in K, above zero.</param>
  /// <param name="gasConstant">Upstream specific gas constant in J/(kg K), above zero.</param>
  /// <param name="gas">The gas passed.</param>
  /// <param name="areaM2">Opening area in m2.</param>
  public static double Flow(
    double upstreamPa,
    double downstreamPa,
    double upstreamK,
    double gasConstant,
    FlowGas gas,
    double areaM2
  ) {
    if (upstreamPa <= 0 || areaM2 <= 0)
      return 0;
    if (IsChoked(upstreamPa, downstreamPa))
      return ChokedCoefficient(gas, upstreamK) * areaM2 * upstreamPa;
    double k = gas == FlowGas.Steam ? 1.3 : 1.4;
    double r = downstreamPa / upstreamPa;
    double spread = Math.Pow(r, 2 / k) - Math.Pow(r, (k + 1) / k);
    return Discharge
      * areaM2
      * upstreamPa
      * Math.Sqrt(2 * k / ((k - 1) * gasConstant * upstreamK) * spread);
  }
}
