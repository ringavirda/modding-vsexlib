using System;
using Vintagestory.API.Server;

namespace ExpandedLib.Helpers;

/// <summary>Thin wrapper over <see cref="ISaveGame.GetData{T}"/>/<see cref="ISaveGame.StoreData{T}"/> for per-world side-band data, keyed by an explicit domain.</summary>
public static class ExWorldData {
  /// <summary>Reads a value previously written with <see cref="Set{T}"/>, or <paramref name="defaultValue"/> if never written.</summary>
  public static T Get<T>(
    ICoreServerAPI api,
    string domain,
    string key,
    T defaultValue = default!
  ) => api.WorldManager.SaveGame.GetData($"{domain}:{key}", defaultValue);

  /// <summary>Writes <paramref name="value"/> under <paramref name="domain"/>/<paramref name="key"/>, persisted with the savegame.</summary>
  public static void Set<T>(
    ICoreServerAPI api,
    string domain,
    string key,
    T value
  ) => api.WorldManager.SaveGame.StoreData($"{domain}:{key}", value);

  /// <summary>Subscribes <paramref name="action"/> to <c>Event.GameWorldSave</c>.</summary>
  public static void OnSave(ICoreServerAPI api, Action action) =>
    api.Event.GameWorldSave += action;
}
