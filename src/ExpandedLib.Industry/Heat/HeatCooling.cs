namespace ExpandedLib.Industry.Heat;

/// <summary>Linear cooling of a hot body toward its ambient over game time.</summary>
public static class HeatCooling {
  /// <summary>The temperature after cooling at a fixed rate toward an ambient.</summary>
  /// <param name="temperature">Starting temperature (C).</param>
  /// <param name="ambient">Temperature the body settles at (C).</param>
  /// <param name="ratePerHour">Degrees lost per game hour (C per hour); 0 or less cools nothing.</param>
  /// <param name="hours">Elapsed game hours; 0 or less cools nothing.</param>
  /// <returns><paramref name="temperature"/> minus <c>ratePerHour * hours</c>, never below <paramref name="ambient"/>; <paramref name="temperature"/> unchanged when it is at or below <paramref name="ambient"/>.</returns>
  public static float Cool(
    float temperature,
    float ambient,
    float ratePerHour,
    double hours
  ) {
    if (temperature <= ambient || ratePerHour <= 0f || hours <= 0d)
      return temperature;
    return (float)System.Math.Max(ambient, temperature - ratePerHour * hours);
  }
}
