using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Structures;

/// <summary>World cells weather snow never forms on, neither a snow layer above nor a snow-covered
/// variant. A multiblock structure marks its <see cref="CellRoles.NoSnow"/> cells here on the server,
/// keyed by its own position; the marks stay while its chunk is unloaded, are saved with the world and
/// are read back before any chunk loads. <see cref="IsMarked"/> is safe off the main thread, where
/// the snow simulation reads it.</summary>
public static class NoSnowCells {
  private const string SaveKey = "nosnowcells";

  private static readonly object Gate = new();

  private static readonly Dictionary<
    (int X, int Y, int Z),
    (int X, int Y, int Z)[]
  > CellsByOwner = new();

  // How many owners mark each cell; a cell leaves when its count reaches zero.
  private static readonly ConcurrentDictionary<
    (int X, int Y, int Z),
    int
  > Owners = new();

  /// <summary>Marks <paramref name="cells"/> for the structure at <paramref name="owner"/>, replacing
  /// whatever was marked for that position before. An empty sequence is the same as
  /// <see cref="Unmark"/>.</summary>
  /// <param name="owner">The marking structure's position, dimension included. Whichever block entity
  /// instance stands there, a later <see cref="Mark"/> or <see cref="Unmark"/> at the same position
  /// replaces or drops these marks.</param>
  /// <param name="cells">World positions, dimension included; duplicates count once.</param>
  /// <exception cref="System.ArgumentNullException"><paramref name="owner"/> or <paramref name="cells"/> is null.</exception>
  public static void Mark(BlockPos owner, IEnumerable<BlockPos> cells) {
    System.ArgumentNullException.ThrowIfNull(owner);
    (int X, int Y, int Z) key = Key(owner);
    (int X, int Y, int Z)[] keys = [.. cells.Select(Key).Distinct()];
    lock (Gate) {
      Release(key);
      Add(key, keys);
    }
  }

  /// <summary>Drops every cell marked for the structure at <paramref name="owner"/>. Does nothing for a
  /// position that marked none.</summary>
  /// <exception cref="System.ArgumentNullException"><paramref name="owner"/> is null.</exception>
  public static void Unmark(BlockPos owner) {
    System.ArgumentNullException.ThrowIfNull(owner);
    (int X, int Y, int Z) key = Key(owner);
    lock (Gate)
      Release(key);
  }

  /// <summary>Whether any owner marks <paramref name="pos"/>, in its own dimension. Safe to call from
  /// any thread.</summary>
  public static bool IsMarked(BlockPos pos) => Owners.ContainsKey(Key(pos));

  /// <summary>Drops every mark of every owner; run when a world starts loading, before its saved marks
  /// are read back.</summary>
  internal static void Clear() {
    lock (Gate) {
      CellsByOwner.Clear();
      Owners.Clear();
    }
  }

  /// <summary>Wires the marks to <paramref name="api"/>'s world: read back from the save at
  /// <c>SaveGameLoaded</c>, written at every <c>GameWorldSave</c>, and at each
  /// <c>ChunkColumnLoaded</c> released for an owner in that column whose structure is gone.</summary>
  internal static void Attach(ICoreServerAPI api) {
    api.Event.SaveGameLoaded += () =>
      Restore(ExWorldData.Get<int[]?>(api, "exlib", SaveKey, null));
    ExWorldData.OnSave(
      api,
      () => ExWorldData.Set(api, "exlib", SaveKey, Snapshot())
    );
    api.Event.ChunkColumnLoaded += ReleaseGone;
  }

  /// <summary>Every owner's marks as one flat array: per owner its x, internal y and z, its cell
  /// count, then x, internal y and z of each cell.</summary>
  internal static int[] Snapshot() {
    lock (Gate) {
      var data = new List<int>();
      foreach (var (owner, cells) in CellsByOwner) {
        data.AddRange([owner.X, owner.Y, owner.Z, cells.Length]);
        foreach (var cell in cells)
          data.AddRange([cell.X, cell.Y, cell.Z]);
      }
      return [.. data];
    }
  }

  /// <summary>Adds the marks <paramref name="data"/> holds in <see cref="Snapshot"/>'s layout, each
  /// owner's replacing any it already has. Null adds nothing; reading stops at the first owner whose
  /// cells run past the end or whose count is negative.</summary>
  internal static void Restore(int[]? data) {
    if (data == null)
      return;
    lock (Gate) {
      int i = 0;
      while (i + 4 <= data.Length) {
        (int X, int Y, int Z) owner = (data[i], data[i + 1], data[i + 2]);
        int count = data[i + 3];
        i += 4;
        if ((uint)count > (uint)(data.Length - i) / 3)
          return;
        var keys = new (int X, int Y, int Z)[count];
        for (int c = 0; c < count; c++, i += 3)
          keys[c] = (data[i], data[i + 1], data[i + 2]);
        Release(owner);
        Add(owner, keys);
      }
    }
  }

  /// <summary>Releases the marks of every owner in the normal-world column
  /// <paramref name="column"/> whose chunk in <paramref name="chunks"/> holds no multiblock structure
  /// block entity at the owner's position. Owners above the column's chunks, in other dimensions, are
  /// left alone.</summary>
  internal static void ReleaseGone(Vec2i column, IWorldChunk?[] chunks) {
    const int size = GlobalConstants.ChunkSize;
    lock (Gate) {
      foreach (var owner in CellsByOwner.Keys.ToList()) {
        if (owner.X / size != column.X || owner.Z / size != column.Y)
          continue;
        int cy = owner.Y / size;
        if (cy >= chunks.Length)
          continue;
        var pos = new BlockPos(
          owner.X,
          owner.Y,
          owner.Z,
          Dimensions.NormalWorld
        );
        if (
          chunks[cy]?.GetLocalBlockEntityAtBlockPos(pos)
          is BlockEntityMultiblockStructure
        )
          continue;
        Release(owner);
      }
    }
  }

  private static void Add(
    (int X, int Y, int Z) owner,
    (int X, int Y, int Z)[] keys
  ) {
    if (keys.Length == 0)
      return;
    CellsByOwner[owner] = keys;
    foreach (var key in keys)
      Owners.AddOrUpdate(key, 1, (_, count) => count + 1);
  }

  private static void Release((int X, int Y, int Z) owner) {
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
