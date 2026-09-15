using System;
using System.Collections.Generic;

namespace ExpandedLib.Blocks;

/// <summary>Player-tunable settings for the right-click construction system, supplied by each mod
/// from its own config, keyed by domain.</summary>
public static class ExRccSettings {
  private static readonly Dictionary<string, Func<float>> _brokenDropsRatios =
    new();

  /// <summary>Registers the salvage fraction (0..1) for broken RCC mega-blocks of
  /// <paramref name="domain"/>.</summary>
  public static void RegisterBrokenDropsRatio(
    string domain,
    Func<float> ratio
  ) => _brokenDropsRatios[domain] = ratio;

  /// <summary>The configured salvage fraction for <paramref name="domain"/>, or null when none is
  /// registered.</summary>
  public static float? BrokenDropsRatio(string domain) =>
    _brokenDropsRatios.TryGetValue(domain, out Func<float>? getter)
      ? getter()
      : null;
}
