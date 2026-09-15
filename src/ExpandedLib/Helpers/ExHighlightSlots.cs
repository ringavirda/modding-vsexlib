using System.Collections.Generic;

namespace ExpandedLib.Helpers;

/// <summary>
/// Process-wide handout of distinct <c>IWorldAccessor.HighlightBlocks</c> slot ids. Ids start at
/// <see cref="Base"/>, above vanilla's own reserved slots.
/// </summary>
public static class ExHighlightSlots {
  /// <summary>The first id <see cref="Reserve"/> hands out.</summary>
  public const int Base = 90000;

  private static readonly Dictionary<string, int> _slots = new();
  private static int _next = Base;

  /// <summary>The highlight slot id reserved for <paramref name="key"/>, stable across calls; call
  /// once per feature and keep the result.</summary>
  public static int Reserve(string key) {
    if (_slots.TryGetValue(key, out int id))
      return id;
    id = _next++;
    _slots[key] = id;
    return id;
  }
}
