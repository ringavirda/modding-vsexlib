#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.Testing;
using ExpandedLib.WorldGen;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace ExpandedLib.Tests.WorldGen;

/// <summary>A block whose world generation placement is recorded instead of written.</summary>
internal sealed class RecordingBlock : Block
{
  public List<BlockPos> Placed { get; } = [];

  public override bool TryPlaceBlockForWorldGen(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    BlockFacing onBlockFace,
    IRandom worldgenRandom,
    BlockPatchAttributes? attributes = null
  )
  {
    Placed.Add(pos.Copy());
    return true;
  }
}

/// <summary>A world with the blocks a seam test needs and a chunk column of rock to generate into.</summary>
internal sealed class SeamRig : IDisposable
{
  public const int RockTop = 130;
  public const int SeaLevel = 128;
  public const int Region = 512;

  public TestWorld World { get; } = new();
  public Block Claystone { get; }
  public Block Shale { get; }
  public Block Granite { get; }
  public Block Ore { get; }
  public Block Poor { get; }
  public Block Medium { get; }
  public Block SignClaystone { get; }
  public Block SignShale { get; }
  public NormalizedSimplexNoise Noise { get; } =
    NormalizedSimplexNoise.FromDefaultOctaves(2, 0.05, 0.8, 99);

  public SeamRig()
  {
    World.World.SeaLevel.Returns(SeaLevel);
    World.Api.WorldManager.RegionSize.Returns(Region);
    Claystone = Add("game:rock-claystone", 1, EnumBlockMaterial.Stone);
    Granite = Add("game:rock-granite", 2, EnumBlockMaterial.Stone);
    Ore = Add("test:ore", 3, EnumBlockMaterial.Ore);
    Poor = Add("game:ore-poor-limonite-claystone", 4, EnumBlockMaterial.Ore);
    Medium = Add(
      "game:ore-medium-limonite-claystone",
      5,
      EnumBlockMaterial.Ore
    );
    SignClaystone = Add("test:loose-claystone", 6, EnumBlockMaterial.Stone);
    Shale = Add("game:rock-shale", 7, EnumBlockMaterial.Stone);
    SignShale = Add("test:loose-shale", 8, EnumBlockMaterial.Stone);
  }

  public Block Add(string code, int id, EnumBlockMaterial material) =>
    Add(TestBlocks.Configure(new Block(), code, id), material);

  public Block Add(Block block, EnumBlockMaterial material)
  {
    block.BlockMaterial = material;
    World.Register(block);
    return block;
  }

  public void Dispose() => World.Dispose();

  /// <summary>Attributes JSON with a one-layer default; a full taper everywhere when
  /// <paramref name="taperFull"/> and <paramref name="taperZero"/> are left at 999 and 1000.</summary>
  public static string Attributes(
    float run = 24,
    float strike = 16,
    float exponent = 2,
    float dip = 0.3f,
    float anchorY = SeaLevel,
    float taperFull = 999,
    float taperZero = 1000,
    string layers =
      """[ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 3, "var": 0 } } ]""",
    int minCover = 0,
    string inBlock =
      """{ "code": "game:rock-*", "name": "rock", "allowedVariants": ["claystone", "shale"] }""",
    string footprintExtra = "",
    string extra = ""
  ) =>
    FormattableString.Invariant(
      $$"""
      {
        "inBlock": {{inBlock}},
        "footprint": {
          "run": { "dist": "uniform", "avg": {{run}}, "var": 0 },
          "strike": { "dist": "uniform", "avg": {{strike}}, "var": 0 },
          "exponent": {{exponent}}, "taperFull": {{taperFull}}, "taperZero": {{taperZero}}{{footprintExtra}}
        },
        "dip": { "dist": "uniform", "avg": {{dip}}, "var": 0 },
        "anchor": "sealevel",
        "yCropRel": { "dist": "uniform", "avg": {{anchorY
        / SeaLevel}}, "var": 0 },
        "minCover": {{minCover}},
        "layers": {{layers}}{{extra}}
      }
      """
    );

  public SeamDepositGenerator Generator(
    string attributes,
    int seed,
    Action<DepositVariant>? tweak = null
  )
  {
    var variant = new DepositVariant
    {
      fromFile = "test.json",
      Code = "test",
      Generator = SeamDepositGenerator.Code,
      Attributes = new JsonObject(JToken.Parse(attributes)),
    };
    tweak?.Invoke(variant);
    var generator = new SeamDepositGenerator(
      World.Api,
      variant,
      new LCGRandom(seed),
      Noise
    );
    generator.Init();
    return generator;
  }

  /// <summary>A chunk column with host rock up to <paramref name="top"/> and air above; the terrain
  /// and rain height maps read <paramref name="top"/>.</summary>
  public ProPickWorkSpace.DummyChunk[] Chunks(
    int top = RockTop,
    System.Func<int, int, int>? hostAt = null
  )
  {
    var map = Substitute.For<IMapChunk>();
    var heights = new ushort[1024];
    Array.Fill(heights, (ushort)top);
    map.WorldGenTerrainHeightMap.Returns(heights);
    map.RainHeightMap.Returns(heights);
    var chunks = new ProPickWorkSpace.DummyChunk[8];
    for (int i = 0; i < chunks.Length; i++)
    {
      chunks[i] = new ProPickWorkSpace.DummyChunk(32)
      {
        MapChunk = map,
        chunkY = i,
      };
    }
    for (int lz = 0; lz < 32; lz++)
    {
      for (int lx = 0; lx < 32; lx++)
      {
        int host = hostAt?.Invoke(lx, lz) ?? Claystone.BlockId;
        for (int y = 1; y <= top; y++)
        {
          Set(chunks, lx, y, lz, host);
        }
      }
    }
    return chunks;
  }

  public static int Id(
    ProPickWorkSpace.DummyChunk[] chunks,
    int lx,
    int y,
    int lz
  ) =>
    ((IServerChunk)chunks[y / 32]).Data.GetBlockIdUnsafe(
      ((y % 32) * 32 + lz) * 32 + lx
    );

  public static void Set(
    ProPickWorkSpace.DummyChunk[] chunks,
    int lx,
    int y,
    int lz,
    int id
  ) =>
    ((IServerChunk)chunks[y / 32]).Data.SetBlockUnsafe(
      ((y % 32) * 32 + lz) * 32 + lx,
      id
    );

  /// <summary>Block ids of a column from the floor up.</summary>
  public static List<int> Column(
    ProPickWorkSpace.DummyChunk[] chunks,
    int lx,
    int lz
  )
  {
    var ids = new List<int>();
    for (int y = 0; y < chunks.Length * 32; y++)
    {
      ids.Add(Id(chunks, lx, y, lz));
    }
    return ids;
  }

  public static int Count(ProPickWorkSpace.DummyChunk[] chunks, int id)
  {
    int n = 0;
    for (int lz = 0; lz < 32; lz++)
    {
      for (int lx = 0; lx < 32; lx++)
      {
        n += Column(chunks, lx, lz).FindAll(c => c == id).Count;
      }
    }
    return n;
  }

  public void Generate(
    SeamDepositGenerator generator,
    ProPickWorkSpace.DummyChunk[] chunks,
    int chunkX,
    BlockPos pos,
    IBlockAccessor? accessor = null
  )
  {
    var sub = new Dictionary<BlockPos, DepositVariant>();
    generator.GenDeposit(
      accessor ?? Substitute.For<IBlockAccessor>(),
      chunks,
      chunkX,
      0,
      pos,
      ref sub
    );
  }

  /// <summary>The first seed whose drawn body satisfies <paramref name="wanted"/>.</summary>
  public static int SeedWhere(
    string attributes,
    System.Func<SeamBody, bool> wanted
  )
  {
    var spec = Newtonsoft.Json.JsonConvert.DeserializeObject<SeamSpec>(
      attributes
    )!;
    for (int seed = 1; seed < 5_000_000; seed++)
    {
      if (wanted(SeamBody.Draw(spec, new LCGRandom(seed))))
      {
        return seed;
      }
    }
    throw new InvalidOperationException("no seed draws the wanted body");
  }
}
#endif
