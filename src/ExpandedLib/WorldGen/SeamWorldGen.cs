#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace ExpandedLib.WorldGen;

/// <summary>
/// The world generation pass that places seams. It runs after vanilla's deposits with its own random
/// and its own chunk range, so a seam never moves a block vanilla places and vanilla's range stays
/// at three chunks. Seam files live in <c>worldgen/seams/</c>, which vanilla's deposit pass never reads.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SeamWorldGen : GenPartial
{
  /// <summary>Asset folder the seam files are read from.</summary>
  internal const string Folder = "worldgen/seams/";

  /// <summary>The range the pass never drops below, vanilla's deposit range.</summary>
  internal const int MinChunkRange = 3;

  private const int ChunkSeedOffset = 80417;
  private const int DepositSeedOffset = 80429;
  private const string PropickCache = "propickworkspace";

  private DepositVariant[] _variants = [];
  private int _range = MinChunkRange;
  private float _chanceMultiplier = 1;
  private LCGRandom? _depositRand;
  private IBlockAccessor? _blockAccessor;
  private readonly BlockPos _tmpPos = new(Dimensions.NormalWorld);
  private Dictionary<BlockPos, DepositVariant> _unused = [];

  /// <summary>After vanilla's deposits (0.2), so a column's seams are laid over its ore.</summary>
  public override double ExecuteOrder() => 0.21;

  /// <summary>Chunks around the generated column that are scanned for bodies.</summary>
  protected override int chunkRange => _range;

  /// <summary>Chunks around the column the pass scans; never below vanilla's three.</summary>
  internal int ChunkRange => _range;

  /// <summary>The seam variants the pass places.</summary>
  internal IReadOnlyList<DepositVariant> Variants => _variants;

  /// <summary>Hooks the pass into the server's world generation and the prospecting pick.</summary>
  public override void StartServerSide(ICoreServerAPI api)
  {
    base.StartServerSide(api);
    if (TerraGenConfig.DoDecorationPass)
    {
      api.Event.ChunkColumnGeneration(
        GenChunkColumn,
        EnumWorldGenPass.TerrainFeatures,
        "standard"
      );
      api.Event.GetWorldgenBlockAccessor(provider =>
        _blockAccessor = provider.GetBlockAccessor(true)
      );
      api.Event.MapRegionGeneration(OnMapRegionGen, "standard");
    }
    api.Event.ServerRunPhase(
      EnumServerRunPhase.RunGame,
      () =>
        RegisterProspecting(
          ObjectCacheUtil.TryGet<ProPickWorkSpace>(api, PropickCache)
        )
    );
  }

  /// <summary>Reads the seam files and builds each variant's generator.</summary>
  public override void AssetsFinalize(ICoreAPI api)
  {
    if (api is not ICoreServerAPI sapi)
    {
      return;
    }
    float chance = sapi
      .Assets.Get("worldgen/deposits.json")
      .ToObject<Deposits>()
      .ChanceMultiplier;
    var variants = new List<DepositVariant>();
    foreach (
      var file in sapi
        .Assets.GetMany<DepositVariant[]>(sapi.World.Logger, Folder)
        .OrderBy(f => f.Key.ToString())
    )
    {
      foreach (DepositVariant variant in file.Value)
      {
        variant.fromFile = file.Key.ToString();
        variants.Add(variant);
      }
    }
    Load(sapi, variants, chance);
  }

  /// <summary>Initialises <paramref name="variants"/> against <paramref name="api"/>'s world and sets
  /// the pass's chunk range to the longest reach of the seams that place anything.</summary>
  internal void Load(
    ICoreServerAPI api,
    IEnumerable<DepositVariant> variants,
    float chanceMultiplier
  )
  {
    this.api = api;
    _chanceMultiplier = chanceMultiplier;
    _depositRand = new LCGRandom(api.WorldManager.Seed + DepositSeedOffset);
    NormalizedSimplexNoise noise = NormalizedSimplexNoise.FromDefaultOctaves(
      3,
      1 / 10f,
      0.9f,
      1
    );
    _variants = [.. variants];
    foreach (DepositVariant variant in _variants)
    {
      variant.addHandbookAttributes = true;
      variant.Init(api, _depositRand, noise);
    }
    _range = Math.Max(
      MinChunkRange,
      _variants
        .Select(v => v.GeneratorInst)
        .OfType<SeamDepositGenerator>()
        .Select(g => SeamDepositGenerator.ChunkRangeFor(g.ReachRadius))
        .DefaultIfEmpty(0)
        .Max()
    );
  }

  /// <summary>Seeds the pass's randoms from the world seed and builds the ore map layers.</summary>
  public override void initWorldGen()
  {
    worldheight = api.WorldManager.MapSizeY;
    int seed = api.WorldManager.Seed;
    chunkRand = new LCGRandom(seed + ChunkSeedOffset);
    var layers = new Dictionary<string, MapLayerBase>();
    foreach (DepositVariant variant in _variants.Where(v => v.WithOreMap))
    {
      if (!layers.TryGetValue(variant.Code, out MapLayerBase? layer))
      {
        int codeSeed = seed + variant.Code.GetNonRandomizedHashCode();
        layers[variant.Code] = layer = GenMaps.GetOreMap(
          codeSeed + 1,
          new NoiseOre(codeSeed),
          variant.OreMapScale,
          variant.OreMapContrast,
          variant.OreMapSub
        );
      }
      variant.OreMapLayer = layer;
    }
  }

  /// <summary>Adds the seams' ore maps to a region, as vanilla's deposit pass does for its own.</summary>
  internal void OnMapRegionGen(
    IMapRegion region,
    int regionX,
    int regionZ,
    ITreeAttribute? _ = null
  )
  {
    foreach (DepositVariant variant in _variants)
    {
      variant.OnMapRegionGen(region, regionX, regionZ);
    }
  }

  /// <summary>Lists the seams with ore maps in the prospecting pick, which reads only the deposits it
  /// finds in its workspace; null when the game has no pick workspace.</summary>
  internal void RegisterProspecting(ProPickWorkSpace? workspace)
  {
    if (workspace == null)
    {
      return;
    }
    foreach (DepositVariant variant in _variants.Where(v => v.WithOreMap))
    {
      workspace.pageCodes[variant.Code] = variant.HandbookPageCode;
      workspace.depositsByCode[variant.Code] = variant;
      if (variant.HandbookPageCode == null)
      {
        api.World.Logger.Warning(
          "Seam {0} has no handbook page code. Links created by the prospecting pick will not work without it.",
          variant.Code
        );
      }
    }
  }

  /// <summary>Generates one chunk column; the game's column event calls this.</summary>
  internal void Generate(IChunkColumnGenerateRequest request) =>
    GenChunkColumn(request);

  /// <inheritdoc/>
  protected override void GenChunkColumn(IChunkColumnGenerateRequest request)
  {
    if (_blockAccessor is IWorldGenBlockAccessor accessor)
    {
      accessor.BeginColumn();
    }
    base.GenChunkColumn(request);
  }

  /// <inheritdoc/>
  public override void GeneratePartial(
    IServerChunk[] chunks,
    int chunkX,
    int chunkZ,
    int chunkdX,
    int chunkdZ
  )
  {
    int fromChunkX = chunkX + chunkdX;
    int fromChunkZ = chunkZ + chunkdZ;
    int fromBaseX = fromChunkX * GlobalConstants.ChunkSize;
    int fromBaseZ = fromChunkZ * GlobalConstants.ChunkSize;
    float heightScale = (float)api.WorldManager.MapSizeY / 256;
    IBlockAccessor blockAccessor = _blockAccessor ?? api.World.BlockAccessor;
    foreach (DepositVariant variant in _variants)
    {
      float factor = variant.WithOreMap
        ? variant.GetOreMapFactor(fromChunkX, fromChunkZ)
        : 1;
      float tries =
        variant.TriesPerChunk
        * factor
        * _chanceMultiplier
        * (variant.ScaleWithWorldheight ? heightScale : 1);
      int quantity = (int)tries;
      quantity += chunkRand.NextInt(100) < 100 * (tries - quantity) ? 1 : 0;
      while (quantity-- > 0)
      {
        _tmpPos.Set(
          fromBaseX + chunkRand.NextInt(GlobalConstants.ChunkSize),
          -99,
          fromBaseZ + chunkRand.NextInt(GlobalConstants.ChunkSize)
        );
        _depositRand!.SetWorldSeed(chunkRand.NextInt(10000000));
        _depositRand.InitPositionSeed(fromChunkX, fromChunkZ);
        variant.GeneratorInst?.GenDeposit(
          blockAccessor,
          chunks,
          chunkX,
          chunkZ,
          _tmpPos,
          ref _unused
        );
      }
    }
  }
}
#endif
