using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>
/// The published mods built against exlib 0.7 that cannot load beside this version: they bind to
/// namespaces and an assembly layout 0.8 no longer has, and the game's dependency check cannot see
/// it because a modinfo dependency is a floor. Found at <c>StartPre</c>, reported once at Error and
/// to every joining player, so the failure is named rather than surfacing as a missing type.
/// </summary>
internal static class IncompatibleMods {
  /// <summary>Mod id to the name players know the mod by.</summary>
  internal static readonly IReadOnlyDictionary<string, string> Known =
    new Dictionary<string, string> {
      ["smex"] = "Steelmaking Expanded",
      ["ppex"] = "Pipes and Power Expanded",
    };

  /// <summary>
  /// The one message the log and the chat carry, or <c>null</c> when no known mod is enabled.
  /// <paramref name="exlibVersion"/> is this mod's own version string.
  /// </summary>
  internal static string? Message(IModLoader loader, string exlibVersion) {
    List<string> names = Known
      .Where(kv => loader.IsModEnabled(kv.Key))
      .Select(kv => kv.Value)
      .ToList();
    if (names.Count == 0)
      return null;
    return $"exlib {exlibVersion} does not work with {string.Join(" and ", names)}: "
      + "keep exlib 0.7.2 with them, or replace them with Iron Industry Expanded.";
  }
}
