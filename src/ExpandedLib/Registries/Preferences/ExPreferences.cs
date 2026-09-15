using System;
using System.Collections.Generic;
using ExpandedLib.Config;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>On-disk shape of the per-player preferences file: for each player UID, a map of
/// preference key to chosen value.</summary>
internal class ExPreferencesConfig {
  /// <summary>playerUID -&gt; (preferenceKey -&gt; value).</summary>
  public Dictionary<string, Dictionary<string, string>> Players { get; set; } =
  [];
}

/// <summary>Store for per-player display preferences shared by every Expanded mod. Client-side
/// only; persists each player's choices and applies them to live client state.</summary>
public static class ExPreferences {
  /// <summary>Config file name, written under the game's <c>ModConfig</c> folder.</summary>
  public const string ConfigFileName = "exmod_preferences.json";

  /// <summary>Former names of <see cref="ConfigFileName"/>, renamed on load.</summary>
  private static readonly string[] _legacyFileNames = ["exmod.json"];

  private static readonly Dictionary<string, IExPreference> _registered = new();
  private static ExPreferencesConfig _config = new();
  private static ICoreAPI? _api;

  /// <summary>Every registered preference definition, in registration order.</summary>
  public static IEnumerable<IExPreference> All => _registered.Values;

  /// <summary>Adds a preference definition; a later registration with the same
  /// <see cref="IExPreference.Key"/> replaces the earlier one.</summary>
  public static void Register(IExPreference preference) =>
    _registered[preference.Key] = preference;

  /// <summary>The registered preference for <paramref name="key"/>, or <c>null</c> if none.</summary>
  public static IExPreference? Find(string key) =>
    _registered.TryGetValue(key, out var p) ? p : null;

  /// <summary>Loads <see cref="ConfigFileName"/> (falling back to defaults) and writes it back.</summary>
  public static void LoadConfig(ICoreAPI api) {
    _api = api;
    ExConfigFiles.RenameLegacy(api, "exlib", ConfigFileName, _legacyFileNames);
    try {
      _config =
        api.LoadModConfig<ExPreferencesConfig>(ConfigFileName)
        ?? new ExPreferencesConfig();
    } catch (Exception e) {
      api.Logger.Warning(
        "[exlib] Failed to read {0}; using defaults. {1}",
        ConfigFileName,
        e
      );
      _config = new ExPreferencesConfig();
    }
    Save();
  }

  /// <summary>The saved value of a preference for a player, or the preference's
  /// <see cref="IExPreference.Default"/> when none is stored (or the key is unknown).</summary>
  public static string GetForPlayer(string playerUid, string key) {
    if (
      _config.Players.TryGetValue(playerUid, out var prefs)
      && prefs.TryGetValue(key, out var value)
    )
      return value;
    return Find(key)?.Default ?? string.Empty;
  }

  /// <summary>Stores and persists a player's choice and applies it to live client state.</summary>
  public static void SetForPlayer(string playerUid, string key, string value) {
    if (!_config.Players.TryGetValue(playerUid, out var prefs))
      _config.Players[playerUid] = prefs = new Dictionary<string, string>();
    prefs[key] = value;
    Find(key)?.Apply(value);
    Save();
  }

  /// <summary>Applies every registered preference's saved value for a player to live client state.</summary>
  public static void ApplyForPlayer(string playerUid) {
    foreach (var pref in _registered.Values)
      pref.Apply(GetForPlayer(playerUid, pref.Key));
  }

  private static void Save() {
    try {
      _api?.StoreModConfig(_config, ConfigFileName);
    } catch (Exception e) {
      _api?.Logger.Warning(
        "[exlib] Could not write {0}. {1}",
        ConfigFileName,
        e
      );
    }
  }
}
