using System;
using System.Collections.Generic;

namespace ExpandedLib.Testing;

/// <summary>Holds the one <see cref="TestWorld"/> that <see cref="TestWorld.LoadAsGame"/> built
/// last, so the tests of a collection share one load. Not thread-safe across worlds: the tests that
/// use it run in one non-parallel collection.</summary>
internal static class GameWorlds {
  private static readonly object Gate = new();
  private static string? _key;
  private static TestWorld? _world;

  /// <summary>Returns the world loaded for <paramref name="modPaths"/>, building it when the held
  /// one belongs to another mod list; the old world is disposed first, so the per-world registries
  /// always belong to the world returned. The caller never disposes the result.</summary>
  /// <param name="modPaths">Mod folders in load order; the same list in the same order is the same key.</param>
  /// <param name="register">Runs on a new world before its load, to register the caller's classes;
  /// null registers none.</param>
  /// <param name="gamePath">As <see cref="TestWorld.LoadAsGame"/> takes it.</param>
  internal static TestWorld Get(
    IReadOnlyList<string> modPaths,
    Action<TestWorld>? register = null,
    string? gamePath = null
  ) {
    string key = string.Join('|', modPaths);
    lock (Gate) {
      if (_world != null && _key == key)
        return _world;
      _world?.Dispose();
      _world = null;
      var world = new TestWorld();
      try {
        register?.Invoke(world);
        world.LoadAsGame(modPaths, gamePath);
      } catch {
        world.Dispose();
        throw;
      }
      _key = key;
      return _world = world;
    }
  }
}
