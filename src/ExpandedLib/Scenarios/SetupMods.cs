using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>A mod's version and the exlib version it requires, null when it names none.</summary>
internal readonly record struct SetupModVersions(string Version, string? Exlib);

/// <summary>The recording's <c>mods</c> map (setups.md, What is recorded).</summary>
internal static class SetupMods {
  /// <summary>
  /// <c>exlib</c> first, at the version the first of <paramref name="mods"/> that names exlib
  /// requires, or at <paramref name="exlibVersion"/> when none does; then each mod at its
  /// version, in <paramref name="mods"/>' order.
  /// </summary>
  public static IReadOnlyList<(string Mod, string Version)> Map(
    IReadOnlyList<string> mods,
    Func<string, SetupModVersions> versions,
    string exlibVersion
  ) {
    SetupModVersions[] each = [.. mods.Select(versions)];
    string exlib = each.Select(v => v.Exlib).FirstOrDefault(v => v != null) ?? exlibVersion;
    return [("exlib", exlib), .. mods.Select((m, i) => (m, each[i].Version))];
  }

  /// <summary>The versions a <c>modinfo.json</c> text names, its keys in any case.</summary>
  /// <exception cref="InvalidOperationException">The modinfo names no version.</exception>
  public static SetupModVersions FromModInfo(string json) {
    JObject node = JObject.Parse(json);
    string version =
      node.GetValue("version", StringComparison.OrdinalIgnoreCase)?.Value<string>()
      ?? throw new InvalidOperationException("The modinfo names no version.");
    string? exlib = (
      node.GetValue("dependencies", StringComparison.OrdinalIgnoreCase) as JObject
    )
      ?.GetValue("exlib", StringComparison.OrdinalIgnoreCase)
      ?.Value<string>();
    return new SetupModVersions(version, exlib);
  }
}
