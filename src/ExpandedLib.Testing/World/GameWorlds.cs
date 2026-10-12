using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Registries;

namespace ExpandedLib.Testing;

/// <summary>Holds the one <see cref="TestWorld"/> that <see cref="TestWorld.LoadAsGame"/> built
/// last, so the tests of a collection share one load. Not thread-safe across worlds: the tests that
/// use it run in one non-parallel collection.</summary>
internal static class GameWorlds {
  private static readonly object Gate = new();
  private static string? _key;
  private static TestWorld? _world;
  private static int _generation;

  /// <summary>Returns the world loaded for <paramref name="modPaths"/>, building it when the held
  /// one belongs to another mod list or another load has emptied the per-world registries since;
  /// the old world is disposed first, so the registries always belong to the world returned. The caller never disposes the result.</summary>
  /// <param name="modPaths">Mod folders in load order; the same full paths in the same order, with the
  /// same <paramref name="gamePath"/>, are the same key.</param>
  /// <param name="register">Runs on a new world before its load, to register the caller's classes;
  /// null registers none. It must be the same for a given key, which does not include it.</param>
  /// <param name="restore">Runs on a world after each build that finishes, to re-seed the registries
  /// the load emptied; null seeds none.</param>
  /// <param name="gamePath">As <see cref="TestWorld.LoadAsGame"/> takes it.</param>
  internal static TestWorld Get(
    IReadOnlyList<string> modPaths,
    Action<TestWorld>? register = null,
    string? gamePath = null,
    Action<TestWorld>? restore = null
  ) {
    string key = string.Join(
      '|',
      modPaths
        .Select(m => Path.TrimEndingDirectorySeparator(Path.GetFullPath(m)))
        .Prepend(gamePath ?? "")
    );
    lock (Gate) {
      if (
        _world != null
        && _key == key
        && _generation == ExWorldState.LoadGeneration
      )
        return _world;
      _world?.Dispose();
      _world = null;
      var world = new TestWorld();
      try {
        register?.Invoke(world);
        world.LoadAsGame(modPaths, gamePath);
        _generation = ExWorldState.LoadGeneration;
        restore?.Invoke(world);
      } catch {
        world.Dispose();
        throw;
      }
      _key = key;
      return _world = world;
    }
  }
}
