using System;
using System.Collections.Generic;

namespace ExpandedLib.Structures;

/// <summary>
/// What a multiblock layout cell is for, as opposed to what block may occupy it. A string key declared
/// by the mod that owns the machine; equality and hashing are by <see cref="Key"/> alone.
/// </summary>
public readonly record struct CellRole(string Key) {
  // Whether each key was last minted single-cell. A conflicting later declaration simply wins.
  private static readonly Dictionary<string, bool> _single = new(
    StringComparer.Ordinal
  );

  /// <summary>Declares the role named <paramref name="key"/>, single-cell when <paramref name="single"/> is true. A caller that only looks up an existing role uses <c>new CellRole(key)</c> instead.</summary>
  /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or all whitespace.</exception>
  public static CellRole Of(string key, bool single = false) {
    if (string.IsNullOrWhiteSpace(key))
      throw new ArgumentException(
        "A cell role key cannot be blank.",
        nameof(key)
      );
    lock (_single)
      _single[key] = single;
    return new CellRole(key);
  }

  /// <summary>Whether this role was declared <see cref="Of">single-cell</see>.</summary>
  public bool IsSingle {
    get {
      lock (_single)
        return _single.TryGetValue(Key, out bool single) && single;
    }
  }

  /// <summary>The role's key, exactly as declared.</summary>
  public override string ToString() => Key;
}

/// <summary>Facts about <see cref="CellRole"/> that both the layout builder and its consumers read.</summary>
public static class CellRoles {
  /// <summary>Whether <paramref name="role"/> was declared <see cref="CellRole.IsSingle">single-cell</see>.</summary>
  public static bool IsSingleCell(CellRole role) => role.IsSingle;
}
