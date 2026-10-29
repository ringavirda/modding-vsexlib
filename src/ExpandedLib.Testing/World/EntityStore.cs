using System.Collections;
using System.Collections.Generic;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>The block entities of a <see cref="TestWorld"/> by cell, with the structure fillers
/// among them indexed by principal so <see cref="FillersOf"/> reads one structure's fillers
/// without scanning every entity.</summary>
/// <remarks>A filler is indexed the first time <see cref="FillersOf"/> runs after it was stored
/// or handed out and names a principal. An instance held across a read and re-pointed is read
/// under neither principal until it is stored or handed out again.</remarks>
internal sealed class EntityStore
  : IEnumerable<KeyValuePair<BlockPos, BlockEntity>> {
  private readonly Dictionary<BlockPos, BlockEntity> _all = new();
  private readonly Dictionary<
    BlockPos,
    Dictionary<BlockPos, BlockEntity>
  > _byPrincipal = new();
  private readonly Dictionary<BlockPos, BlockPos> _filedUnder = new();
  private readonly HashSet<BlockPos> _unindexed = [];

  /// <summary>The <see cref="FillersOf"/> calls made so far.</summary>
  public int Reads { get; private set; }

  /// <summary>The entries <see cref="FillersOf"/> has walked, in its buckets and in the index
  /// queue, across all its calls.</summary>
  public long Visited { get; private set; }

  /// <summary>The fillers queued for indexing, on being stored or handed out, across all calls.</summary>
  public long Queued { get; private set; }

  public BlockEntity this[BlockPos pos] {
    set {
      _all[pos] = value;
      if (value is BlockEntityStructureFiller)
        Queue(pos);
    }
  }

  private void Queue(BlockPos pos) {
    if (_unindexed.Add(pos.Copy()))
      Queued++;
  }

  public bool TryGetValue(BlockPos pos, out BlockEntity entity) {
    bool found = _all.TryGetValue(pos, out entity!);
    if (found && entity is BlockEntityStructureFiller)
      Queue(pos);
    return found;
  }

  public bool Remove(BlockPos pos) => _all.Remove(pos);

  public bool Remove(BlockPos pos, out BlockEntity entity) =>
    _all.Remove(pos, out entity!);

  /// <summary>The cells of the stored fillers whose entity names <paramref name="principal"/>,
  /// in the order they were first indexed.</summary>
  /// <param name="isFillerBlock">Whether the block at a cell is a structure filler.</param>
  public BlockPos[] FillersOf(
    BlockPos principal,
    System.Func<BlockPos, bool> isFillerBlock
  ) {
    Reads++;
    Index();
    if (!_byPrincipal.TryGetValue(principal, out var cells))
      return [];
    var found = new List<BlockPos>(cells.Count);
    List<BlockPos>? stale = null;
    foreach ((BlockPos at, BlockEntity entity) in cells) {
      Visited++;
      if (!_all.TryGetValue(at, out BlockEntity? current) || current != entity)
        (stale ??= []).Add(at);
      else if (
        entity is BlockEntityStructureFiller { Principal: { } p }
        && p.Equals(principal)
        && isFillerBlock(at)
      )
        found.Add(at.Copy());
    }
    if (stale != null)
      foreach (BlockPos at in stale)
        cells.Remove(at);
    return [.. found];
  }

  private void Index() {
    if (_unindexed.Count == 0)
      return;
    var waiting = new List<BlockPos>();
    foreach (BlockPos at in _unindexed) {
      Visited++;
      if (!_all.TryGetValue(at, out BlockEntity? entity))
        continue;
      if (entity is not BlockEntityStructureFiller { Principal: { } principal }) {
        if (entity is BlockEntityStructureFiller)
          waiting.Add(at);
        continue;
      }
      if (
        _filedUnder.TryGetValue(at, out BlockPos? old) && !old.Equals(principal)
      )
        _byPrincipal[old].Remove(at);
      if (!_byPrincipal.TryGetValue(principal, out var cells))
        _byPrincipal[principal.Copy()] = cells = new();
      cells[at] = entity;
      _filedUnder[at] = principal.Copy();
    }
    _unindexed.Clear();
    _unindexed.UnionWith(waiting);
  }

  public IEnumerator<KeyValuePair<BlockPos, BlockEntity>> GetEnumerator() =>
    _all.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
