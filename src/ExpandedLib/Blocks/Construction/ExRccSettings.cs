using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ExpandedLib.Registries;

namespace ExpandedLib.Blocks;

/// <summary>Player-tunable settings for the right-click construction system, supplied by each mod
/// from its own config, keyed by domain. Emptied when a world starts loading
/// (<see cref="Registries.ExWorldState"/>).</summary>
public static class ExRccSettings {
  private static readonly Dictionary<string, Func<float>> _brokenDropsRatios =
    new();
  private static readonly Dictionary<
    string,
    (string[] Codes, string Assembly)
  > _brokenDropsLost = new();

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

  /// <summary>Registers the codes of <paramref name="domain"/>'s broken RCC mega-blocks that are
  /// lost at the salvage ratio; every other returned stack comes back whole, so an empty list loses
  /// nothing, and a domain with no list returns every stack at the ratio. A later registration
  /// replaces the earlier one, and one from another assembly logs a Warning naming both. The list
  /// replaces a <c>brokenDropsLost</c> list in the block JSON.</summary>
  /// <param name="domain">The mod domain whose blocks the list applies to.</param>
  /// <param name="codes">Full item or block codes with a domain, each matched as a wildcard
  /// (<c>game:clay-*</c>) against the resolved code of a returned stack.</param>
  /// <exception cref="ArgumentException">An entry has no domain (no <c>:</c>).</exception>
  [MethodImpl(MethodImplOptions.NoInlining)]
  public static void RegisterBrokenDropsLost(
    string domain,
    IEnumerable<string> codes
  ) {
    string[] list = [.. codes];
    foreach (string code in list)
      if (!code.Contains(':'))
        throw new ArgumentException(
          $"Broken-drops entry '{code}' has no domain.",
          nameof(codes)
        );
    string assembly =
      new StackFrame(1).GetMethod()?.DeclaringType?.Assembly.GetName().Name
      ?? "an unknown assembly";
    if (
      _brokenDropsLost.TryGetValue(domain, out var previous)
      && previous.Assembly != assembly
    )
      RegistryLog.Replaced(
        "ExRccSettings",
        domain,
        previous.Assembly,
        assembly
      );
    _brokenDropsLost[domain] = (list, assembly);
  }

  /// <summary>The codes registered by <see cref="RegisterBrokenDropsLost"/> for
  /// <paramref name="domain"/>.</summary>
  /// <returns>Null when the domain never registered a list (every stack is then at the ratio); an
  /// empty list when it registered one with no codes (every stack is whole).</returns>
  public static IReadOnlyList<string>? BrokenDropsLost(string domain) =>
    _brokenDropsLost.TryGetValue(domain, out var entry) ? entry.Codes : null;

  /// <summary>Drops every registered ratio and list; run when a world starts loading.</summary>
  internal static void ResetForWorld() {
    _brokenDropsRatios.Clear();
    _brokenDropsLost.Clear();
  }
}
