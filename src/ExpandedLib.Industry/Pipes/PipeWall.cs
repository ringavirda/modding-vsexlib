namespace ExpandedLib.Industry.Pipes;

/// <summary>The wall of a pipe node or vessel shell: what it holds in heat and the areas it exchanges through.</summary>
/// <param name="CapacityJPerK">Heat capacity of the wall in J/K, above zero.</param>
/// <param name="InnerAreaM2">Area facing the content in m2, zero or more.</param>
/// <param name="OuterAreaM2">Area facing the ambient in m2, zero or more.</param>
/// <param name="Lagged">Whether the outside is wrapped in lagging, which cuts the outside coefficient to a fifth.</param>
internal readonly record struct PipeWall(
  double CapacityJPerK,
  double InnerAreaM2,
  double OuterAreaM2,
  bool Lagged = false
) {
  /// <summary>Content-to-wall coefficient for gas in W/(m2 K).</summary>
  public const double InnerGasCoefficient = 30;

  /// <summary>Steam-to-wall coefficient while steam condenses on a colder wall in W/(m2 K).</summary>
  public const double InnerCondensingCoefficient = 5000;

  /// <summary>Liquid-to-wall coefficient of a liquid-full node in W/(m2 K).</summary>
  public const double InnerWaterCoefficient = 500;

  /// <summary>Wall-to-ambient coefficient of a bare wall in W/(m2 K).</summary>
  public const double OuterBareCoefficient = 15;

  /// <summary>Wall-to-ambient coefficient of a lagged wall in W/(m2 K).</summary>
  public const double OuterLaggedCoefficient = 3;

  /// <summary>Content-to-wall conductance in W/K.</summary>
  public double InnerUaWPerK => InnerGasCoefficient * InnerAreaM2;

  /// <summary>Wall-to-ambient conductance in W/K.</summary>
  public double OuterUaWPerK =>
    (Lagged ? OuterLaggedCoefficient : OuterBareCoefficient) * OuterAreaM2;
}
