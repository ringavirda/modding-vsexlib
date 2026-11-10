using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A pulley that holds a strung belt: the cells the belt claims are its to keep clear. The owner calls
/// <see cref="BeltPaths.Release"/> when it unloads or removes the belt.</summary>
public interface IBeltPathOwner {
  /// <summary>The cell of the pulley that holds the belt; one owner a cell.</summary>
  BlockPos Pos { get; }

  /// <summary>The pulley the belt starts at, or null when no belt is strung.</summary>
  BeltPulley? From { get; }

  /// <summary>The pulley the belt ends at, or null when no belt is strung.</summary>
  BeltPulley? To { get; }

  /// <summary>Cells the two pulleys hold besides their own, which the belt may claim.</summary>
  IReadOnlyCollection<BlockPos> ClaimedCells { get; }

  /// <summary>Called on the server when a block that meets the strap is placed in a cell the belt claims.</summary>
  /// <param name="pos">The cell the block was placed in.</param>
  void PathBlocked(BlockPos pos);
}

/// <summary>
/// The server's record of every strung belt's cells. An owner <see cref="Claim"/>s its path when strung and
/// <see cref="Release"/>s it when taken back or unloaded; owners in a loaded chunk column claim again. A block a
/// player places in a claimed cell that meets the strap or its loop tells the owner.
/// </summary>
public sealed class BeltPaths {
  private const string CacheKey = "exlib:beltpaths";

  private readonly ICoreServerAPI _api;
  private readonly Dictionary<BlockPos, Strung> _claims = [];
  private readonly Dictionary<BlockPos, BlockPos> _cells = [];

  private sealed record Strung(
    IBeltPathOwner Owner,
    BeltPulley From,
    BeltPulley To,
    IReadOnlyList<BlockPos> Cells
  );

  private BeltPaths(ICoreServerAPI api) {
    _api = api;
  }

  /// <summary>Creates the record, subscribes it to the placed-block and chunk-column-loaded events, and keeps it
  /// where <see cref="Of"/> finds it.</summary>
  /// <param name="api">The server API.</param>
  public static BeltPaths Start(ICoreServerAPI api) {
    var paths = new BeltPaths(api);
    api.ObjectCache[CacheKey] = paths;
    api.Event.DidPlaceBlock += paths.OnPlaced;
    api.Event.ChunkColumnLoaded += paths.OnChunkColumnLoaded;
    return paths;
  }

  /// <summary>The record <see cref="Start"/> made for this API, or null before it ran or on a client.</summary>
  /// <param name="api">The API of either side.</param>
  public static BeltPaths? Of(ICoreAPI api) =>
    api.ObjectCache.TryGetValue(CacheKey, out object? paths)
      ? paths as BeltPaths
      : null;

  /// <summary>The owner whose belt claims a cell, or null.</summary>
  /// <param name="cell">The cell.</param>
  public IBeltPathOwner? OwnerAt(BlockPos cell) =>
    _cells.TryGetValue(cell, out BlockPos? owner) ? _claims[owner].Owner : null;

  /// <summary>Judges a belt between two pulleys as <see cref="BeltPath.Check"/> does, with the cells every other
  /// strung belt claims counted as claimed elsewhere.</summary>
  /// <param name="a">The first pulley.</param>
  /// <param name="b">The second pulley.</param>
  /// <param name="claimed">Cells the pulleys hold besides their own.</param>
  /// <param name="self">The owner of the belt being judged, whose own claim does not count; null for a new belt.</param>
  /// <returns>The first refusal code that applies, or null.</returns>
  public string? Check(
    BeltPulley a,
    BeltPulley b,
    IReadOnlyCollection<BlockPos> claimed,
    IBeltPathOwner? self = null
  ) =>
    BeltPath.Check(
      _api.World.BlockAccessor,
      a,
      b,
      claimed,
      cell =>
        _cells.TryGetValue(cell, out BlockPos? owner)
        && (self == null || !owner.Equals(self.Pos))
    );

  /// <summary>Records the cells the owner's belt claims, replacing what its cell held before; an owner with no
  /// strung belt holds none.</summary>
  /// <param name="owner">The owner, whose <see cref="IBeltPathOwner.From"/> and <see cref="IBeltPathOwner.To"/>
  /// are read now.</param>
  public void Claim(IBeltPathOwner owner) {
    Release(owner);
    if (owner.From is not { } from || owner.To is not { } to)
      return;
    var excepted = new HashSet<BlockPos>(owner.ClaimedCells)
    {
      from.Cell,
      to.Cell,
    };
    var cells = new List<BlockPos>();
    foreach (BlockPos cell in BeltPath.Footprint(from, to))
      if (!excepted.Contains(cell))
        cells.Add(cell);
    BlockPos key = owner.Pos.Copy();
    _claims[key] = new Strung(owner, from, to, cells);
    foreach (BlockPos cell in cells)
      _cells[cell] = key;
  }

  /// <summary>Drops the cells the owner's cell claimed; nothing when it claimed none.</summary>
  /// <param name="owner">The owner.</param>
  public void Release(IBeltPathOwner owner) {
    if (!_claims.Remove(owner.Pos, out Strung? claim))
      return;
    foreach (BlockPos cell in claim.Cells)
      _cells.Remove(cell);
  }

  private void OnPlaced(
    IServerPlayer byPlayer,
    int oldBlockId,
    BlockSelection blockSel,
    ItemStack withItemStack
  ) {
    BlockPos pos = blockSel.Position;
    if (
      _cells.TryGetValue(pos, out BlockPos? key)
      && _claims[key] is { } claim
      && BeltPath.Meets(_api.World.BlockAccessor, pos, claim.From, claim.To)
    )
      claim.Owner.PathBlocked(pos.Copy());
  }

  private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks) {
    foreach (IWorldChunk chunk in chunks)
      if (chunk?.BlockEntities is { } entities)
        foreach (BlockEntity be in entities.Values) {
          if (be is IBeltPathOwner owner)
            Claim(owner);
          foreach (BlockEntityBehavior behavior in be.Behaviors)
            if (behavior is IBeltPathOwner hosted)
              Claim(hosted);
        }
  }
}
