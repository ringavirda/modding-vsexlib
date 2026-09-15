// Polyfills for BCL methods present on .NET 10 (the 1.22 runtime) but not on the legacy runtimes.
#if !GAME_GE_1_22
using System.Collections.Generic;

namespace ExpandedLib.Legacy;

public static class LegacyLinq {
  /// <summary>Polyfill of <c>Enumerable.Index()</c> (added in .NET 9): pairs each element with
  /// its zero-based position.</summary>
  public static IEnumerable<(int Index, T Item)> Index<T>(
    this IEnumerable<T> source
  ) {
    int i = 0;
    foreach (var item in source)
      yield return (i++, item);
  }
}
#endif
