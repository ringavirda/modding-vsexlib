using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace ExpandedLib.Testing.Live;

/// <summary>A probe run on the server: generates a rectangle of chunk columns tile by tile, counts the
/// requested blocks per tile from chunk data, unloads each tile and writes the report. Beside
/// <c>probe-&lt;seed&gt;-&lt;time&gt;.json</c> it writes, when asked, the <c>.blocks</c> file (records of four little-endian
/// int32: code index, x, y, z, one per block of a recorded code) and the <c>.terrain</c> file (per column
/// int32 columnX, columnZ, province index, then 64 little-endian uint16 terrain heights, 8 x 8 samples every
/// fourth block, rows from the lowest Z).</summary>
internal sealed class WorldProbe {
  private const int ChunkSize = 32;

  private readonly ICoreServerAPI _api;
  private readonly ProbeRequest _request;
  private readonly IReadOnlyList<ProbeTile> _tiles;
  private readonly int[] _patternOf;
  private readonly List<ProbeTileResult> _results = [];
  private readonly Stopwatch _total = Stopwatch.StartNew();
  private readonly Stopwatch _tileClock = new();
  private readonly long _listener;
  private readonly string _stem;
  private readonly BinaryWriter? _blocks;
  private readonly BinaryWriter? _terrain;
  private readonly string[] _provinces;
  private int _next;
  private int _loaded;

  /// <summary>Whether tiles are still being generated; false once the report is written or the run
  /// failed.</summary>
  public bool Active { get; private set; } = true;

  /// <summary>Starts the run: resolves the block ids of <see cref="ProbeRequest.Codes"/> and loads the
  /// first tile. Call on the main thread.</summary>
  public WorldProbe(ICoreServerAPI api, ProbeRequest request) {
    _api = api;
    _request = request;
    int minX = request.MinColumnX;
    int minZ = request.MinColumnZ;
    if (request.CenterOnSpawn) {
      EntityPos spawn = api.World.DefaultSpawnPosition;
      minX = (int)spawn.X / ChunkSize - request.ColumnsX / 2;
      minZ = (int)spawn.Z / ChunkSize - request.ColumnsZ / 2;
    }
    _tiles = request.Tiles(minX, minZ);
    _patternOf = BlockPatterns(api, request.Codes);
    string dir = Path.Combine(GamePaths.DataPath, "probe");
    Directory.CreateDirectory(dir);
    _stem = Path.Combine(
      dir,
      $"probe-{api.WorldManager.Seed}-{DateTime.UtcNow:yyyyMMddHHmmss}"
    );
    if (request.Recorded.Any(r => r))
      _blocks = new BinaryWriter(File.Create(_stem + ".blocks"));
    _provinces =
      NoiseGeoProvince.provinces?.Variants.Select(v => v.Code).ToArray() ?? [];
    if (request.Terrain)
      _terrain = new BinaryWriter(File.Create(_stem + ".terrain"));
    _listener = api.Event.RegisterGameTickListener(Tick, 20);
    LoadNext();
  }

  /// <summary>The index in <paramref name="codes"/> of the first pattern each block id matches, -1 for
  /// none.</summary>
  private static int[] BlockPatterns(
    ICoreServerAPI api,
    IReadOnlyList<string> codes
  ) {
    int[] map = new int[api.World.Blocks.Count];
    Array.Fill(map, -1);
    foreach (Block block in api.World.Blocks) {
      if (block.Code == null || block.Id < 0 || block.Id >= map.Length)
        continue;
      string code = block.Code.ToString();
      for (int i = 0; i < codes.Count; i++)
        if (WildcardUtil.Match(codes[i], code)) {
          map[block.Id] = i;
          break;
        }
    }
    return map;
  }

  private void LoadNext() {
    ProbeTile t = _tiles[_next];
    _loaded = 0;
    _tileClock.Restart();
    _api.WorldManager.LoadChunkColumnPriority(
      t.MinColumnX,
      t.MinColumnZ,
      t.MaxColumnX,
      t.MaxColumnZ,
      new ChunkLoadOptions {
        KeepLoaded = true,
        OnLoaded = () => System.Threading.Volatile.Write(ref _loaded, 1),
      }
    );
  }

  private void Tick(float dt) {
    if (!Active || System.Threading.Volatile.Read(ref _loaded) == 0)
      return;
    System.Threading.Volatile.Write(ref _loaded, 0);
    ProbeTile t = _tiles[_next];
    long[] counts = new long[_request.Codes.Count];
    for (int x = t.MinColumnX; x <= t.MaxColumnX; x++)
      for (int z = t.MinColumnZ; z <= t.MaxColumnZ; z++) {
        CountColumn(x, z, counts);
        WriteTerrain(x, z);
        _api.WorldManager.UnloadChunkColumn(x, z);
      }
    _results.Add(
      new ProbeTileResult(
        t,
        _tileClock.Elapsed.TotalSeconds,
        _request
          .Codes.Select((c, i) => (c, n: counts[i]))
          .ToDictionary(p => p.c, p => p.n)
      )
    );
    _next++;
    if (_next < _tiles.Count) {
      LoadNext();
      return;
    }
    Finish();
  }

  private void CountColumn(int cx, int cz, long[] counts) {
    int layers = _api.WorldManager.MapSizeY / ChunkSize;
    for (int cy = 0; cy < layers; cy++) {
      IWorldChunk? chunk = _api.WorldManager.GetChunk(cx, cy, cz);
      IChunkBlocks? data = chunk?.Data;
      if (data == null)
        continue;
      var ids = new List<int>();
      data.FuzzyListBlockIds(ids);
      if (
        !ids.Any(id => id >= 0 && id < _patternOf.Length && _patternOf[id] >= 0)
      )
        continue;
      data.TakeBulkReadLock();
      try {
        for (int i = 0; i < data.Length; i++) {
          int id = data.GetBlockIdUnsafe(i);
          if (id < 0 || id >= _patternOf.Length || _patternOf[id] < 0)
            continue;
          int code = _patternOf[id];
          counts[code]++;
          if (_blocks != null && _request.Recorded[code]) {
            _blocks.Write(code);
            _blocks.Write(cx * ChunkSize + (i & 31));
            _blocks.Write(cy * ChunkSize + (i >> 10));
            _blocks.Write(cz * ChunkSize + ((i >> 5) & 31));
          }
        }
      } finally {
        data.ReleaseBulkReadLock();
      }
    }
  }

  private void WriteTerrain(int cx, int cz) {
    IServerMapChunk? map =
      _terrain == null ? null : _api.WorldManager.GetMapChunk(cx, cz);
    if (_terrain == null || map == null)
      return;
    _terrain.Write(cx);
    _terrain.Write(cz);
    _terrain.Write(Province(map, cx, cz));
    for (int z = 0; z < ChunkSize; z += 4)
      for (int x = 0; x < ChunkSize; x += 4)
        _terrain.Write(map.WorldGenTerrainHeightMap[z * ChunkSize + x]);
  }

  /// <summary>The province index of the map cell under the column's centre, -1 without a map.</summary>
  private int Province(IServerMapChunk map, int cx, int cz) {
    IntDataMap2D? provinces = map.MapRegion?.GeologicProvinceMap;
    if (provinces == null)
      return -1;
    int region = _api.WorldManager.RegionSize;
    int x = (cx * ChunkSize + ChunkSize / 2) % region;
    int z = (cz * ChunkSize + ChunkSize / 2) % region;
    return provinces.GetUnpaddedInt(
      (x + region) % region / TerraGenConfig.geoProvMapScale,
      (z + region) % region / TerraGenConfig.geoProvMapScale
    );
  }

  private void Finish() {
    Active = false;
    _api.Event.UnregisterGameTickListener(_listener);
    _blocks?.Dispose();
    _terrain?.Dispose();
    EntityPos spawn = _api.World.DefaultSpawnPosition;
    string path = _stem + ".json";
    File.WriteAllText(
      path,
      ProbeReport.ToJson(
        _api.WorldManager.Seed,
        [(int)spawn.X, (int)spawn.Y, (int)spawn.Z],
        _total.Elapsed.TotalSeconds,
        _request.Codes,
        _results,
        new ProbeFiles(
          _request.Codes.Where((c, i) => _request.Recorded[i]).ToArray(),
          _blocks == null ? null : Path.GetFileName(_stem) + ".blocks",
          _terrain == null ? null : Path.GetFileName(_stem) + ".terrain",
          _provinces
        )
      )
    );
    _api.Logger.Notification("[probe] done: {0}", path);
  }
}
