namespace ExpandedLib.Catalogues;

/// <summary>
/// Medium policy the pipe network consults instead of hardcoded medium strings. A network
/// constructed without one falls back to <see cref="ExLiquids.Taxonomy"/>.
/// </summary>
public interface IMediumTaxonomy {
  /// <summary>True when <paramref name="code"/> is a liquid (incompressible, single-family) rather
  /// than a gas. An unknown code reads as a gas.</summary>
  bool IsLiquid(string code);

  /// <summary>Whether <paramref name="medium"/> can be produced into a run currently carrying
  /// <paramref name="current"/>.</summary>
  bool Compatible(string current, string medium);

  /// <summary>The dominant of two media by merge priority; ties keep <paramref name="a"/>.</summary>
  string HigherPriority(string a, string b);

  /// <summary>The condensation partner of <paramref name="code"/>, independent of temperature.
  /// On <c>true</c>, <paramref name="target"/> is the resulting liquid and
  /// <paramref name="volumeFactor"/> the volume multiplier.</summary>
  bool CondensationTarget(
    string code,
    out string target,
    out float volumeFactor
  );

  /// <summary>The vaporisation partner of <paramref name="code"/>, independent of temperature.
  /// On <c>true</c>, <paramref name="target"/> is the resulting gas and
  /// <paramref name="volumeFactor"/> the volume multiplier.</summary>
  bool VaporisationTarget(
    string code,
    out string target,
    out float volumeFactor
  );

  /// <summary>Whether <paramref name="code"/> condenses at <paramref name="tempC"/> (C). On
  /// <c>true</c>, <paramref name="target"/> is the resulting medium and
  /// <paramref name="volumeFactor"/> the volume multiplier.</summary>
  bool TryCondensation(
    string code,
    float tempC,
    out string target,
    out float volumeFactor
  );

  /// <summary>Whether <paramref name="code"/> boils at <paramref name="tempC"/> (C). On
  /// <c>true</c>, <paramref name="target"/> is the resulting gas and
  /// <paramref name="volumeFactor"/> the volume multiplier.</summary>
  bool TryVaporisation(
    string code,
    float tempC,
    out string target,
    out float volumeFactor
  );
}
