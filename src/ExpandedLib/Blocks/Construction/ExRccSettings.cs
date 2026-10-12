using System;
using System.Collections.Generic;
using ExpandedLib.Registries;

namespace ExpandedLib.Blocks;

/// <summary>Player-tunable settings for the right-click construction system, supplied by each mod
/// from its own config, keyed by domain. Emptied when a world starts loading
/// (<see cref="Registries.ExWorldState"/>).</summary>
public static class ExRccSettings {
  private static readonly Dictionary<string, Func<float>> _brokenDropsRatios =
    new();

  /// <summary>Registers the salvage fraction (0..1) for broken RCC mega-blocks of
  /// <paramref name="domain"/>; a later registration for the domain replaces the earlier one; one
  /// from another assembly logs a Warning naming both assemblies.</summary>
  public static void RegisterBrokenDropsRatio(string domain, Func<float> ratio) {
    if (
      _brokenDropsRatios.TryGetValue(domain, out Func<float>? previous)
      && Registrant(previous) != Registrant(ratio)
    )
      RegistryLog.Replaced(
        "ExRccSettings",
        domain,
        Registrant(previous),
        Registrant(ratio)
      );
    _brokenDropsRatios[domain] = ratio;
  }

  private static string Registrant(Func<float> getter) =>
    getter.Method.DeclaringType?.Assembly.GetName().Name
    ?? "an unknown assembly";

  /// <summary>The configured salvage fraction for <paramref name="domain"/>, or null when none is
  /// registered.</summary>
  public static float? BrokenDropsRatio(string domain) =>
    _brokenDropsRatios.TryGetValue(domain, out Func<float>? getter)
      ? getter()
      : null;

  /// <summary>Drops every registered ratio; run when a world starts loading.</summary>
  internal static void ResetForWorld() => _brokenDropsRatios.Clear();
}
