using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>World cells weather snow never forms on, neither a snow layer above nor a snow-covered
/// variant. A multiblock structure marks its <see cref="CellRoles.NoSnow"/> cells here on the server;
/// thread-safe, since the snow simulation reads it off the main thread.</summary>
public static class NoSnowCells {
  private static readonly object Gate = new();

  private static readonly Dictionary<
    object,
    (int X, int Y, int Z)[]
  > CellsByOwner = new(ReferenceEqualityComparer.Instance);

  // How many owners mark each cell; a cell leaves when its count reaches zero.
  private static readonly ConcurrentDictionary<
    (int X, int Y, int Z),
    int
  > Owners = new();

  /// <summary>Marks <paramref name="cells"/> for <paramref name="owner"/>, replacing whatever that owner
  /// marked before. An empty sequence is the same as <see cref="Unmark"/>.</summary>
  /// <param name="owner">Compared by reference; the key a later <see cref="Unmark"/> passes.</param>
  /// <param name="cells">World positions, dimension included; duplicates count once.</param>
  /// <exception cref="System.ArgumentNullException"><paramref name="owner"/> or <paramref name="cells"/> is null.</exception>
  public static void Mark(object owner, IEnumerable<BlockPos> cells) {
    (int X, int Y, int Z)[] keys = [.. cells.Select(Key).Distinct()];
    lock (Gate) {
      Release(owner);
      if (keys.Length == 0)
        return;
      CellsByOwner[owner] = keys;
      foreach (var key in keys)
        Owners.AddOrUpdate(key, 1, (_, count) => count + 1);
    }
  }

  /// <summary>Drops every cell <paramref name="owner"/> marked. Does nothing for an owner that marked none.</summary>
  /// <exception cref="System.ArgumentNullException"><paramref name="owner"/> is null.</exception>
  public static void Unmark(object owner) {
    lock (Gate)
      Release(owner);
  }

  /// <summary>Whether any owner marks <paramref name="pos"/>, in its own dimension.</summary>
  public static bool IsMarked(BlockPos pos) => Owners.ContainsKey(Key(pos));

  /// <summary>Drops every mark of every owner, for a world that ends without unloading its blocks.</summary>
  internal static void Clear() {
    lock (Gate) {
      CellsByOwner.Clear();
      Owners.Clear();
    }
  }

  private static void Release(object owner) {
    if (!CellsByOwner.Remove(owner, out var keys))
      return;
    foreach (var key in keys) {
      if (!Owners.TryGetValue(key, out int count))
        continue;
      if (count <= 1)
        Owners.TryRemove(key, out _);
      else
        Owners[key] = count - 1;
    }
  }

  private static (int X, int Y, int Z) Key(BlockPos pos) =>
    (pos.X, pos.InternalY, pos.Z);
}
