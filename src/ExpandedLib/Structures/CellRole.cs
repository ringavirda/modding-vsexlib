using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ExpandedLib.Registries;

namespace ExpandedLib.Structures;

/// <summary>
/// What a multiblock layout cell is for, as opposed to what block may occupy it. A string key declared
/// by the mod that owns the machine; equality and hashing are by <see cref="Key"/> alone.
/// </summary>
public readonly record struct CellRole(string Key) {
  // Whether each key was last minted single-cell, and the assembly that declared it. A conflicting later declaration wins.
  private static readonly Dictionary<string, bool> _single = new(
    StringComparer.Ordinal
  );
  private static readonly Dictionary<string, string> _owners = new(
    StringComparer.Ordinal
  );

  /// <summary>Declares the role named <paramref name="key"/>, single-cell when <paramref name="single"/> is true. A caller that only looks up an existing role uses <c>new CellRole(key)</c> instead.
  /// A key declared by another assembly is taken over with a Warning naming both; its own assembly may declare it again silently.</summary>
  /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or all whitespace.</exception>
  [MethodImpl(MethodImplOptions.NoInlining)]
  public static CellRole Of(string key, bool single = false) =>
    Of(
      key,
      single,
      Assembly.GetCallingAssembly().GetName().Name ?? "unknown assembly"
    );

  internal static CellRole Of(string key, bool single, string owner) {
    if (string.IsNullOrWhiteSpace(key))
      throw new ArgumentException(
        "A cell role key cannot be blank.",
        nameof(key)
      );
    string? previous;
    lock (_single) {
      _single[key] = single;
      _owners.TryGetValue(key, out previous);
      _owners[key] = owner;
    }
    if (previous != null && previous != owner)
      RegistryLog.Replaced("CellRole", key, previous, owner);
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
  /// <summary>Marks the block weather snow would lie on or turn into, never the air above it: while the
  /// structure stands, <see cref="NoSnowCells"/> keeps snow off that cell.</summary>
  public static readonly CellRole NoSnow = CellRole.Of("nosnow");

  /// <summary>Whether <paramref name="role"/> was declared <see cref="CellRole.IsSingle">single-cell</see>.</summary>
  public static bool IsSingleCell(CellRole role) => role.IsSingle;
}
