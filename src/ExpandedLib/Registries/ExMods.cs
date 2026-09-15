using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>Checks and reacts to another mod being installed: a one-liner check, a run-now
/// callback and a JSON patch condition.</summary>
public static class ExMods {
  /// <summary>True when <paramref name="modId"/> is loaded and enabled on <paramref name="api"/>'s
  /// side.</summary>
  public static bool IsLoaded(ICoreAPI api, string modId) =>
    !string.IsNullOrWhiteSpace(modId) && api.ModLoader.IsModEnabled(modId);

  /// <summary>The loaded mod's version string, or null when <paramref name="modId"/> is not
  /// loaded.</summary>
  public static string? Version(ICoreAPI api, string modId) =>
    IsLoaded(api, modId) ? api.ModLoader.GetMod(modId).Info.Version : null;

  /// <summary>True when <paramref name="modId"/> is loaded and its version is at least
  /// <paramref name="minimumVersion"/>.</summary>
  public static bool AtLeast(ICoreAPI api, string modId, string minimumVersion) {
    string? version = Version(api, modId);
    return version != null
      && GameVersion.IsAtLeastVersion(version, minimumVersion);
  }

  /// <summary>Runs <paramref name="action"/> immediately when <paramref name="modId"/> is loaded,
  /// otherwise does nothing. Returns whether it ran.</summary>
  public static bool WhenLoaded(ICoreAPI api, string modId, Action action) {
    if (!IsLoaded(api, modId))
      return false;
    action();
    return true;
  }

  /// <summary>The world-config key <see cref="ExModsModSystem"/> sets to <c>true</c> for every
  /// enabled mod: <c>"exlib:mod:&lt;modid&gt;"</c>.</summary>
  public static string FlagKey(string modId) => "exlib:mod:" + modId;
}
