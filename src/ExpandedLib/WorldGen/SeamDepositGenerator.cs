#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace ExpandedLib.WorldGen;

/// <summary>
/// Generator <c>exlib-seam</c>: an inclined, layered body whose footprint, dip and layers come
/// from the deposit file's attributes. It replaces only the host rocks <c>inBlock</c> names.
/// </summary>
internal sealed class SeamDepositGenerator : DepositGeneratorBase
{
  /// <summary>The generator code a deposit file names.</summary>
  public const string Code = "exlib-seam";

  private const double ClimateScaleMax = 5.0 / 3;

  private sealed class HostPlan(int layers)
  {
    public Block?[] Layers { get; } = new Block?[layers];
    public Block?[] Signs { get; } = new Block?[layers];
    public Block?[][] Outcrops { get; } = new Block?[layers][];
  }

  private static readonly ConditionalWeakTable<
    IntDataMap2D,
    LerpedWeightedIndex2DMap
  > ProvinceMaps = new();

  private SeamSpec? _spec;
  private float _scale = 1;
  private double _absAvgQuantity;
  private readonly Dictionary<int, HostPlan> _plans = [];

  /// <summary>The attributes this generator was built from; null when they were rejected.</summary>
  internal SeamSpec? Spec => _spec;

  /// <summary>Number of host rock blocks the generator replaces.</summary>
  internal int HostCount => _plans.Count;

  /// <summary>Called by the deposit registry with the arguments vanilla passes every generator.</summary>
  public SeamDepositGenerator(
    ICoreServerAPI api,
    DepositVariant variant,
    LCGRandom depositRand,
    NormalizedSimplexNoise noiseGen
  )
    : base(api, variant, depositRand, noiseGen) { }

  /// <summary>Reads the variant's attributes strictly, resolves the host rocks and each layer's
  /// blocks per host. A bad attribute file logs an error naming the variant's file and leaves the
  /// generator placing nothing.</summary>
  public override void Init()
  {
    SeamSpec? spec;
    try
    {
      var serializer = JsonSerializer.Create(
        new JsonSerializerSettings
        {
          MissingMemberHandling = MissingMemberHandling.Error,
        }
      );
      spec = variant.Attributes?.Token.ToObject<SeamSpec>(serializer);
    }
    catch (JsonException e)
    {
      Api.Logger.Error(
        "Deposit {0}: bad exlib-seam attributes: {1}",
        variant.fromFile,
        e.Message
      );
      return;
    }
    if (
      spec?.InBlock?.Code == null
      || spec.Footprint?.Run == null
      || spec.Footprint.Strike == null
      || spec.Layers.Count == 0
    )
    {
      Api.Logger.Error(
        "Deposit {0}: exlib-seam needs inBlock, footprint run and strike, and at least one layer.",
        variant.fromFile
      );
      return;
    }
    foreach (
      NatFloat length in (NatFloat[])[spec.Footprint.Run, spec.Footprint.Strike]
    )
    {
      if (length.avg + length.var > DepositFootprint.MaxLength)
      {
        Api.Logger.Warning(
          "Deposit {0}: footprint length up to {1} blocks is cut to {2}.",
          variant.fromFile,
          length.avg + length.var,
          DepositFootprint.MaxLength
        );
      }
    }

    var rocks = new HostRocks(Api, spec.InBlock, variant.fromFile);
    if (rocks.ById.Count == 0)
    {
      Api.Logger.Warning(
        "Deposit {0}: no blocks found by code/wildcard '{1}'. Deposit will never spawn.",
        variant.fromFile,
        spec.InBlock.Code
      );
    }
    foreach ((int id, HostRock host) in rocks.ById)
    {
      _plans[id] = PlanFor(rocks, host, spec);
    }
    _scale = WorldScale.Resolve(
      spec.WorldScale,
      Api.World,
      Api.Logger,
      variant.fromFile
    );
    variant.TriesPerChunk *= _scale;
    _absAvgQuantity = AbsAvgQuantity(spec, variant.TriesPerChunk);
    _spec = spec;
  }

  /// <summary>Blocks from a body's centre that its columns reach; 0 for a rejected spec or a world
  /// the scale turns off.</summary>
  internal float ReachRadius => _scale > 0 ? GetMaxRadius() : 0;

  /// <summary>Chunks from a body's centre chunk that reach a column <paramref name="radius"/>
  /// blocks from the centre: a centre may sit at the near edge of its chunk.</summary>
  internal static int ChunkRangeFor(float radius) =>
    (int)MathF.Ceiling(radius / GlobalConstants.ChunkSize);

  private static double AbsAvgQuantity(SeamSpec spec, float triesPerChunk)
  {
    SeamFootprintSpec fp = spec.Footprint;
    double area = DepositFootprint.Area(
      Math.Min(fp.Run.avg, DepositFootprint.MaxLength),
      Math.Min(fp.Strike.avg, DepositFootprint.MaxLength),
      fp.Exponent
    );
    double thickness = spec
      .Layers.Where(l => l.Propick)
      .Sum(l => l.Thickness.avg);
    return area
      * thickness
      * DepositFootprint.ExpectedFactor(fp.Exponent, fp.TaperFull, fp.TaperZero)
      * triesPerChunk;
  }

  private HostPlan PlanFor(HostRocks rocks, HostRock host, SeamSpec spec)
  {
    var plan = new HostPlan(spec.Layers.Count);
    for (int i = 0; i < spec.Layers.Count; i++)
    {
      SeamLayer layer = spec.Layers[i];
      plan.Layers[i] = rocks.Find(host, layer.Block);
      if (plan.Layers[i] is { } placed && variant.addHandbookAttributes)
      {
        HostRocks.AddHandbook(host.Block, placed);
      }
      if (layer.SurfaceBlock != null)
      {
        plan.Signs[i] = rocks.Find(host, layer.SurfaceBlock);
      }
      if (layer.Outcrop is { } outcrop)
      {
        plan.Outcrops[i] = Array.ConvertAll(
          outcrop.Grades,
          grade => rocks.Find(host, outcrop.Block, grade)
        );
      }
    }
    return plan;
  }

  /// <inheritdoc/>
  public override void GenDeposit(
    IBlockAccessor blockAccessor,
    IServerChunk[] chunks,
    int originChunkX,
    int originChunkZ,
    BlockPos pos,
    ref Dictionary<BlockPos, DepositVariant> subDepositsToPlace
  )
  {
    if (_spec is not { } spec || _scale == 0)
    {
      return;
    }
    SeamBody body = SeamBody.Draw(spec, DepositRand);
    DepositFootprint fp = body.Footprint;
    int baseX = originChunkX * chunksize;
    int baseZ = originChunkZ * chunksize;
    float cos = MathF.Cos(body.DipDirection);
    float sin = MathF.Sin(body.DipDirection);
    float a = body.Run / 2;
    float b = body.Strike / 2;
    float reachX = MathF.Abs(a * cos) + MathF.Abs(b * sin);
    float reachZ = MathF.Abs(a * sin) + MathF.Abs(b * cos);
    if (
      pos.X + reachX < baseX
      || pos.X - reachX >= baseX + chunksize
      || pos.Z + reachZ < baseZ
      || pos.Z - reachZ >= baseZ + chunksize
    )
    {
      return;
    }

    if (!KeptByProvince(spec, body, chunks[0], originChunkX, originChunkZ, pos))
    {
      return;
    }

    var ground = new ColumnGround(Api.World, chunks);
    int seaLevel = Api.World.SeaLevel;
    bool anyOutcrop = body.OutcropDepths.Any(d => d != null);
    float climate = anyOutcrop ? ClimateScale(blockAccessor, pos) : 1;
    var target = new BlockPos(0);
    for (int lz = 0; lz < chunksize; lz++)
    {
      for (int lx = 0; lx < chunksize; lx++)
      {
        int x = baseX + lx;
        int z = baseZ + lz;
        (float u, float v) = fp.Locate(x - pos.X, z - pos.Z);
        float s = fp.S(u, v);
        float edge = DepositFootprint.Edge(
          (float)DistortNoiseGen.Noise(x / 3.0, z / 3.0)
        );
        if (s > edge)
        {
          continue;
        }
        float anchorY =
          spec.Anchor == SeamAnchor.Sealevel
            ? body.Anchor * seaLevel
            : ground.TerrainY(lx, lz) - body.Anchor;
        float baseY = fp.BaseY(anchorY, body.Dip, u);
        float taper = fp.Taper(s, edge);
        float r = PositionHash.Fraction(body.Seed, x, 0, z, -1);
        bool needRock = spec.MinCover > 0 || anyOutcrop;
        int rockTop = needRock ? ground.RockTop(lx, lz) : 0;
        List<SeamCell> cells = SeamStack.Lay(
          spec.Layers,
          body.Thicknesses,
          baseY,
          taper,
          r,
          spec.MinCover,
          rockTop
        );
        SeamSpan[] spans = SeamStack.Spans(
          spec.Layers,
          body.Thicknesses,
          baseY,
          taper
        );
        for (int c = 0; c < cells.Count; c++)
        {
          SeamCell cell = cells[c];
          if (cell.Y <= 1 || cell.Y >= ground.Height)
          {
            continue;
          }
          int hostId = ground.BlockId(lx, cell.Y, lz);
          if (!_plans.TryGetValue(hostId, out HostPlan? plan))
          {
            continue;
          }
          Block? block = plan.Layers[cell.Layer];
          if (block == null)
          {
            continue;
          }
          if (
            plan.Outcrops[cell.Layer] is { Length: > 0 } outcrops
            && InStrip(body, climate, spans[cell.Layer], cell.Layer, rockTop)
          )
          {
            int grade = PositionHash.Index(
              body.Seed,
              cell.Layer,
              outcrops.Length
            );
            block = outcrops[grade] ?? block;
          }
          if (
            variant.WithBlockCallback
            || (spec.WithLastLayerBlockCallback && c == 0)
          )
          {
            target.Set(x, cell.Y, z);
            block.TryPlaceBlockForWorldGen(
              blockAccessor,
              target,
              BlockFacing.UP,
              DepositRand
            );
          }
          else
          {
            ground.Set(lx, cell.Y, lz, block.BlockId, true);
          }

          if (plan.Signs[cell.Layer] is { } sign)
          {
            PlaceSign(
              ground,
              body,
              spec.Layers[cell.Layer],
              sign,
              (lx, lz),
              (x, z),
              cell,
              seaLevel
            );
          }
        }
      }
    }
  }

  private static bool InStrip(
    SeamBody body,
    float climate,
    SeamSpan span,
    int layer,
    int rockTop
  )
  {
    float run = body.OutcropDepths[layer]!.Value * climate * body.Dip;
    return run > 0 && span.Top >= rockTop + 1 - run;
  }

  private void PlaceSign(
    ColumnGround ground,
    SeamBody body,
    SeamLayer layer,
    Block sign,
    (int X, int Z) local,
    (int X, int Z) world,
    SeamCell cell,
    int seaLevel
  )
  {
    (int lx, int lz) = local;
    int surfaceY = ground.TerrainY(lx, lz);
    float depth = surfaceY - cell.Y;
    float chance =
      layer.SurfaceBlockChance
      * Math.Max(0, 1.11f - depth / (9f * seaLevel / 110f));
    float roll = PositionHash.Fraction(
      body.Seed,
      world.X,
      cell.Y,
      world.Z,
      cell.Layer + 1
    );
    if (surfaceY >= ground.Height - 1 || roll >= chance)
    {
      return;
    }
    Block below = Api.World.GetBlock(ground.BlockId(lx, surfaceY, lz));
    if (
      below.SideSolid[BlockFacing.UP.Index]
      && ground.BlockId(lx, surfaceY + 1, lz) == 0
    )
    {
      ground.Set(lx, surfaceY + 1, lz, sign.BlockId, false);
    }
  }

  private float ClimateScale(IBlockAccessor blockAccessor, BlockPos pos)
  {
    int regionSize = Api.WorldManager.RegionSize;
    IntDataMap2D? map = blockAccessor
      .GetMapRegion(pos.X / regionSize, pos.Z / regionSize)
      ?.ClimateMap;
    if (map == null)
    {
      return 1;
    }
    int climate = map.GetUnpaddedColorLerpedForNormalizedPos(
      (float)((double)pos.X / regionSize % 1.0),
      (float)((double)pos.Z / regionSize % 1.0)
    );
    float rainRel =
      Climate.GetRainFall((climate >> 8) & 0xff, Api.World.SeaLevel) / 255f;
    float temp = Climate.GetScaledAdjustedTemperatureFloat(
      (climate >> 16) & 0xff,
      0
    );
    return (float)
      Math.Clamp(
        Math.Clamp(temp / 20, 0, 1) + (1 - rainRel),
        0,
        ClimateScaleMax
      );
  }

  private bool KeptByProvince(
    SeamSpec spec,
    SeamBody body,
    IServerChunk chunk,
    int originChunkX,
    int originChunkZ,
    BlockPos pos
  )
  {
    IntDataMap2D? map = chunk.MapChunk?.MapRegion?.GeologicProvinceMap;
    GeologicProvinceVariant[]? provinces = NoiseGeoProvince.provinces?.Variants;
    if (spec.ProvinceChance == null || map == null || provinces == null)
    {
      return true;
    }
    int regionSize = Api.WorldManager.RegionSize;
    int regionOriginX = originChunkX * chunksize / regionSize * regionSize;
    int regionOriginZ = originChunkZ * chunksize / regionSize * regionSize;
    LerpedWeightedIndex2DMap lerped = ProvinceMaps.GetValue(
      map,
      m => new LerpedWeightedIndex2DMap(
        m.Data,
        m.Size,
        TerraGenConfig.geoProvSmoothingRadius,
        m.TopLeftPadding,
        m.BottomRightPadding
      )
    );
    float lo = 0.5f - map.TopLeftPadding;
    float hi = map.Size - map.TopLeftPadding - 0.5f - 0.001f;
    var weights = new float[provinces.Length];
    lerped.WeightsAt(
      Math.Clamp(
        (pos.X - regionOriginX) / (float)TerraGenConfig.geoProvMapScale,
        lo,
        hi
      ),
      Math.Clamp(
        (pos.Z - regionOriginZ) / (float)TerraGenConfig.geoProvMapScale,
        lo,
        hi
      ),
      weights
    );
    int dominant = Array.IndexOf(weights, weights.Max());
    float keep = spec.ProvinceChance.GetValueOrDefault(
      provinces[dominant].Code,
      1
    );
    return PositionHash.Fraction(body.Seed, pos.X, 0, pos.Z, -2) < keep;
  }

  /// <inheritdoc/>
  public override float GetMaxRadius() =>
    _spec is { } spec
      ? DepositFootprint.Reach(
        Math.Min(
          spec.Footprint.Run.avg + spec.Footprint.Run.var,
          DepositFootprint.MaxLength
        ),
        Math.Min(
          spec.Footprint.Strike.avg + spec.Footprint.Strike.var,
          DepositFootprint.MaxLength
        )
      )
      : 0;

  /// <inheritdoc/>
  public override void GetPropickReading(
    BlockPos pos,
    int oreDist,
    int[] blockColumn,
    out double ppt,
    out double totalFactor
  )
  {
    ppt = 0;
    totalFactor = 0;
    if (_spec is not { } spec || _scale == 0 || pos.Y <= 0)
    {
      return;
    }
    int top = Math.Min(pos.Y, blockColumn.Length);
    int from = Math.Max(0, top - spec.PropickDepth);
    if (top <= from)
    {
      return;
    }
    int hosts = 0;
    for (int y = from; y < top; y++)
    {
      if (_plans.ContainsKey(blockColumn[y]))
      {
        hosts++;
      }
    }
    totalFactor = (oreDist & 0xff) / 255.0 * hosts / (top - from);
    ppt = totalFactor * _absAvgQuantity / (pos.Y * 1024.0) * 1000;
  }

  /// <inheritdoc/>
  public override void GetYMinMax(
    BlockPos pos,
    out double miny,
    out double maxy
  )
  {
    miny = 0;
    maxy = 0;
    if (_spec is not { } spec)
    {
      return;
    }
    double dipRun =
      (spec.Dip.avg + spec.Dip.var)
      * Math.Min(
        spec.Footprint.Run.avg + spec.Footprint.Run.var,
        DepositFootprint.MaxLength
      );
    double stack = spec.Layers.Sum(l =>
      l.Offset + Math.Max(0, l.Thickness.avg + l.Thickness.var)
    );
    if (spec.Anchor == SeamAnchor.Sealevel)
    {
      int seaLevel = Api.World.SeaLevel;
      miny = (spec.YCropRel.avg - spec.YCropRel.var) * seaLevel - dipRun;
      maxy = (spec.YCropRel.avg + spec.YCropRel.var) * seaLevel + stack;
    }
    else
    {
      miny = pos.Y - (spec.Depth.avg + spec.Depth.var) - dipRun;
      maxy = pos.Y - (spec.Depth.avg - spec.Depth.var) + stack;
    }
  }
}
#endif
