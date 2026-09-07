using System;
using Vintagestory.API.Server;

namespace ExpandedLib.Helpers;

/// <summary>
/// Thin wrapper over <see cref="ISaveGame.GetData{T}"/>/<see cref="ISaveGame.StoreData{T}"/> for
/// per-world side-band data - a migration completion marker, a per-world counter, an unlock state -
/// none of which belongs on a block entity's tree or in a ModConfig file. Every key is prefixed with
/// an explicit domain, so two mods' unqualified keys never collide.
/// </summary>
public static class ExWorldData {
  /// <summary>Reads a value previously written with <see cref="Set{T}"/>, or
  /// <paramref name="defaultValue"/> if <paramref name="domain"/>/<paramref name="key"/> was never
  /// written.</summary>
  public static T Get<T>(
    ICoreServerAPI api,
    string domain,
    string key,
    T defaultValue = default!
  ) => api.WorldManager.SaveGame.GetData($"{domain}:{key}", defaultValue);

  /// <summary>Writes <paramref name="value"/> under <paramref name="domain"/>/<paramref name="key"/>,
  /// persisted with the savegame. Call from an <see cref="OnSave"/> hook or another point that runs
  /// once per save, not on every change.</summary>
  public static void Set<T>(
    ICoreServerAPI api,
    string domain,
    string key,
    T value
  ) => api.WorldManager.SaveGame.StoreData($"{domain}:{key}", value);

  /// <summary>Subscribes <paramref name="action"/> to <c>Event.GameWorldSave</c>, the phase every
  /// <see cref="Set{T}"/> call belongs in.</summary>
  public static void OnSave(ICoreServerAPI api, Action action) =>
    api.Event.GameWorldSave += action;
}
