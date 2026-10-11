#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// The registry of <see cref="ISetupPlacer"/>s a test project adds before the first case. Process
/// wide: placers stay registered until <see cref="Clear"/>.
/// </summary>
public static class SetupPlacers
{
  private static readonly List<ISetupPlacer> Registered = [];
  private static readonly object Gate = new();

  /// <summary>Adds <paramref name="placer"/> behind those already registered.</summary>
  /// <param name="placer">The placer to add.</param>
  /// <exception cref="ArgumentNullException"><paramref name="placer"/> is null.</exception>
  public static void Register(ISetupPlacer placer)
  {
    ArgumentNullException.ThrowIfNull(placer);
    lock (Gate)
      Registered.Add(placer);
  }

  /// <summary>Removes every registered placer.</summary>
  public static void Clear()
  {
    lock (Gate)
      Registered.Clear();
  }

  /// <summary>The first registered placer that takes <paramref name="code"/>, or null.</summary>
  /// <param name="code">A block code.</param>
  public static ISetupPlacer? For(string code)
  {
    lock (Gate)
      return Registered.FirstOrDefault(p => p.Takes(code));
  }
}
#endif
