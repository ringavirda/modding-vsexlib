using System.ComponentModel;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Migrations;

/// <summary>
/// Server-side base for systems that act on individual cells across every loaded chunk column: one
/// sweep at <see cref="EnumServerRunPhase.RunGame"/>, then one pass per column as the world streams
/// in. A subclass supplies the work table, an optional per-id reject and the per-cell action.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ChunkColumnSweeperModSystem : ModSystem {
  /// <summary>The server API, captured in <see cref="StartServerSide"/>. Must stay a field with this
  /// exact name: the headless test harness injects it by reflection.</summary>
  protected ICoreServerAPI _sapi = null!;

  /// <summary>Log prefix, e.g. "[exlib]" / "[siex]" - the owning mod's id.</summary>
  protected string Tag => "[" + Mod.Info.ModID + "]";

  private bool _initialized;
  private bool _hasWork;

  // Only the server owns world block/BE data; the client has nothing to sweep.
  public override bool ShouldLoad(EnumAppSide side) =>
    side == EnumAppSide.Server;

  public override void StartServerSide(ICoreServerAPI api) {
    _sapi = api;
    // Spawn-area chunks need one RunGame sweep; every later column arrives through the event.
    api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, RunStartupSweep);
    api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
    OnStartedServer(api);
  }

  /// <summary>Extra server wiring a subclass needs; the migrator uses it to remap carried stacks on
  /// player join.</summary>
  protected virtual void OnStartedServer(ICoreServerAPI api) { }

  /// <summary>Builds the subclass's work table on first use; returns true if this world has anything to do.</summary>
  protected abstract bool BuildWork();

  /// <summary>Fast per-block-id reject, run before the position is even decoded; visits everything by default.</summary>
  protected virtual bool ShouldVisit(int blockId) => true;

  /// <summary>Acts on one non-air cell that passed <see cref="ShouldVisit"/>; returns how many changes it made.</summary>
  protected abstract int VisitCell(
    IBlockAccessor ba,
    BlockPos pos,
    int blockId
  );

  /// <summary>Optional pass over a chunk's block entities after its cells; the migrator uses it to
  /// rewrite container stacks the voxel loop never sees. Makes no changes by default.</summary>
  protected virtual int VisitChunkEntities(IWorldChunk chunk) => 0;

  /// <summary>Reports one column's change count during the RunGame startup sweep; logs nothing by default.</summary>
  protected virtual void OnColumnSwept(int chunkX, int chunkZ, int changed) { }

  /// <summary>Reports the startup sweep's total once it finishes with a non-zero count; logs nothing by default.</summary>
  protected virtual void OnStartupSweepComplete(int total) { }

  /// <summary>Reports a streamed-in column's change count; logs nothing by default.</summary>
  protected virtual void OnColumnStreamedIn(
    int chunkX,
    int chunkZ,
    int changed
  ) { }

  /// <summary>Completion-marker version for this sweeper; null or empty (the default) scans every
  /// column on every world load, and bumping the value re-sweeps every column once more.</summary>
  protected virtual string? Version => null;

  private string MarkerKey => $"sweep.{GetType().Name}.{Version}";

  /// <summary>Builds the work table once via <see cref="BuildWork"/> and memoises whether this world
  /// has anything to do; every entry point calls it, ensuring exactly one build.</summary>
  protected bool EnsureInitialized() {
    if (!_initialized) {
      _hasWork = BuildWork();
      _initialized = true;
    }
    return _hasWork;
  }

  /// <summary>Sweeps every currently loaded chunk column and returns the total change count,
  /// reporting per column through <see cref="OnColumnSwept"/>.</summary>
  protected int SweepAllLoadedChunks() {
    if (!EnsureInitialized())
      return 0;

    int chunksTall = _sapi.WorldManager.MapSizeY / GlobalConstants.ChunkSize;
    int total = 0;

    // Copies the keys: VisitCell can mutate chunks during enumeration.
    foreach (
      long index2d in _sapi.WorldManager.AllLoadedMapchunks.Keys.ToArray()
    ) {
      Vec2i coord = _sapi.WorldManager.MapChunkPosFromChunkIndex2D(index2d);
      IWorldChunk?[] chunks = new IWorldChunk?[chunksTall];
      for (int cy = 0; cy < chunksTall; cy++)
        chunks[cy] = _sapi.WorldManager.GetChunk(coord.X, cy, coord.Y);

      int changed = SweepColumn(coord.X, coord.Y, chunks);
      if (changed > 0)
        OnColumnSwept(coord.X, coord.Y, changed);
      total += changed;
    }

    return total;
  }

  private void RunStartupSweep() {
    int total = SweepAllLoadedChunks();
    if (total > 0)
      OnStartupSweepComplete(total);
  }

  private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks) {
    if (!EnsureInitialized()) {
      // No column can need work; stops listening entirely.
      _sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
      return;
    }

    int changed = SweepColumn(chunkCoord.X, chunkCoord.Y, chunks);
    if (changed > 0)
      OnColumnStreamedIn(chunkCoord.X, chunkCoord.Y, changed);
  }

  /// <summary>Scans one column's already-fetched chunk sections and returns how many changes were
  /// made; skips the scan and returns 0 when <see cref="Version"/> is set and the column already
  /// carries the current-version marker.</summary>
  private int SweepColumn(int chunkX, int chunkZ, IWorldChunk?[] chunks) {
    bool versioned = !string.IsNullOrEmpty(Version);
    IWorldChunk? marker = chunks.Length > 0 ? chunks[0] : null;

    if (
      versioned
      && marker != null
      && ExChunkData.Get(marker, Mod.Info.ModID, MarkerKey, false)
    )
      return 0;

    int changed = 0;
    for (int cy = 0; cy < chunks.Length; cy++)
      changed += ScanChunk(chunkX, cy, chunkZ, chunks[cy]);

    if (versioned && marker != null) {
      ExChunkData.Set(marker, Mod.Info.ModID, MarkerKey, true);
      // SetModdata alone does not dirty the chunk; MarkModified persists the marker across a restart.
      marker.MarkModified();
    }

    return changed;
  }

  /// <summary>Scans one chunk section: visits every non-air cell that passes <see cref="ShouldVisit"/>,
  /// then runs the optional block-entity pass.</summary>
  private int ScanChunk(int chunkX, int chunkY, int chunkZ, IWorldChunk? chunk) {
    if (chunk == null)
      return 0;
    chunk.Unpack();
    IChunkBlocks data = chunk.Data;
    int len = data.Length;

    const int cs = GlobalConstants.ChunkSize;
    IBlockAccessor ba = _sapi.World.BlockAccessor;
    int changed = 0;

    for (int i = 0; i < len; i++) {
      int id = data[i];
      if (id == 0 || !ShouldVisit(id))
        continue;

      // index3d layout: ((y * cs) + z) * cs + x
      int x = i % cs;
      int z = i / cs % cs;
      int y = i / (cs * cs);
      BlockPos pos = new(chunkX * cs + x, chunkY * cs + y, chunkZ * cs + z);

      changed += VisitCell(ba, pos, id);
    }

    changed += VisitChunkEntities(chunk);
    return changed;
  }
}
