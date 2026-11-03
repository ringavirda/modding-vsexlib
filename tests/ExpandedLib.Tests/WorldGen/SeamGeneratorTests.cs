#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.WorldGen;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamGeneratorTests
{
  // Fails when Init leaves the host map empty: nothing is written. The sum is of the sampled edge
  // noise, so 0.650 x area (the figure without the edge shrink) is out of the 15 % band.
  [Fact]
  public void A_flat_seam_writes_the_taper_sum_of_its_columns_over_claystone()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      exponent: 2,
      dip: 0.3f,
      taperFull: 0.4f,
      taperZero: 0.9f,
      anchorY: 100
    );
    const int seed = 11;
    var body = SeamBody.Draw(
      JsonConvert.DeserializeObject<SeamSpec>(attrs)!,
      new LCGRandom(seed)
    );
    var chunks = rig.Chunks();

    rig.Generate(
      rig.Generator(attrs, seed),
      chunks,
      0,
      new BlockPos(16, 100, 16)
    );

    double expected = 0;
    double nominal = DepositFootprint.Area(24, 16, 2);
    for (int z = 0; z < 32; z++)
    {
      for (int x = 0; x < 32; x++)
      {
        (float u, float v) = body.Footprint.Locate(x - 16, z - 16);
        float s = body.Footprint.S(u, v);
        float edge = DepositFootprint.Edge(
          (float)rig.Noise.Noise(x / 3.0, z / 3.0)
        );
        if (s <= edge)
        {
          expected += 3 * body.Footprint.Taper(s, edge);
        }
      }
    }
    int written = SeamRig.Count(chunks, rig.Ore.BlockId);
    Assert.InRange(written, expected * 0.85, expected * 1.15);
    Assert.True(written < nominal * 3 * 0.650 * 0.9);
  }

  // Fails when the host test is removed: the seam overwrites granite.
  [Fact]
  public void A_granite_host_inside_the_footprint_stays_granite()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(run: 60, strike: 40);
    var chunks = rig.Chunks(
      hostAt: (lx, _) => lx < 16 ? rig.Granite.BlockId : rig.Claystone.BlockId
    );

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    for (int lz = 0; lz < 32; lz++)
    {
      for (int lx = 0; lx < 16; lx++)
      {
        Assert.DoesNotContain(rig.Ore.BlockId, SeamRig.Column(chunks, lx, lz));
      }
    }
    Assert.True(SeamRig.Count(chunks, rig.Ore.BlockId) > 0);
  }

  // Fails when the plane is evaluated at the chunk-local lx, lz: chunk 1 differs from the body's
  // world columns. A second run with the column fraction drawn from DepositRand fails it too.
  [Fact]
  public void The_same_body_in_two_neighbouring_chunks_gives_the_stack_of_each_world_column()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      taperFull: 0.4f,
      taperZero: 0.9f,
      anchorY: 110
    );
    var spec = JsonConvert.DeserializeObject<SeamSpec>(attrs)!;
    const int seed = 777;
    var body = SeamBody.Draw(spec, new LCGRandom(seed));
    var pos = new BlockPos(32, 120, 16);
    int total = 0;
    for (int chunkX = 0; chunkX < 2; chunkX++)
    {
      var chunks = rig.Chunks();
      rig.Generate(rig.Generator(attrs, seed), chunks, chunkX, pos);

      for (int lz = 0; lz < 32; lz++)
      {
        for (int lx = 0; lx < 32; lx++)
        {
          int x = chunkX * 32 + lx;
          var expected = ExpectedOre(body, spec, rig, x, lz, pos);
          var actual = Enumerable
            .Range(0, 256)
            .Where(y => SeamRig.Id(chunks, lx, y, lz) == rig.Ore.BlockId)
            .ToList();
          Assert.Equal(expected, actual);
          total += actual.Count;
        }
      }
    }
    Assert.True(total > 100);
  }

  private static List<int> ExpectedOre(
    SeamBody body,
    SeamSpec spec,
    SeamRig rig,
    int x,
    int z,
    BlockPos pos
  )
  {
    (float u, float v) = body.Footprint.Locate(x - pos.X, z - pos.Z);
    float s = body.Footprint.S(u, v);
    float edge = DepositFootprint.Edge(
      (float)rig.Noise.Noise(x / 3.0, z / 3.0)
    );
    if (s > edge)
    {
      return [];
    }
    float baseY = body.Footprint.BaseY(
      body.Anchor * SeamRig.SeaLevel,
      body.Dip,
      u
    );
    float r = PositionHash.Fraction(body.Seed, x, 0, z, -1);
    return SeamStack
      .Lay(
        spec.Layers,
        body.Thicknesses,
        baseY,
        body.Footprint.Taper(s, edge),
        r,
        0,
        0
      )
      .Select(c => c.Y)
      .ToList();
  }

  // Fails when the strict missing-member handling is set to Ignore: the misspelt body loads and writes.
  [Fact]
  public void A_misspelt_attribute_logs_the_file_and_writes_nothing()
  {
    using var rig = new SeamRig();
    rig.World.Log.Expect(EnumLogType.Error, "test.json");
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      footprintExtra: """, "taperFul": 0.6"""
    );
    var chunks = rig.Chunks();

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.Equal(0, SeamRig.Count(chunks, rig.Ore.BlockId));
    Assert.Contains(
      rig.World.Log.Errors,
      e => e.Contains("test.json") && e.Contains("taperFul")
    );
  }

  // Fails when the completeness check is removed: Init leaves no spec and GenDeposit would read null members.
  [Fact]
  public void Attributes_without_a_footprint_log_an_error_and_write_nothing()
  {
    using var rig = new SeamRig();
    rig.World.Log.Expect(EnumLogType.Error, "needs inBlock");
    var chunks = rig.Chunks();

    rig.Generate(rig.Generator("{}", 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.Equal(0, SeamRig.Count(chunks, rig.Ore.BlockId));
  }

  // Fails when the length warning is removed.
  [Fact]
  public void A_run_over_the_reach_is_cut_with_a_warning_naming_the_file()
  {
    using var rig = new SeamRig();
    rig.World.Log.Expect(
      EnumLogType.Warning,
      "test.json: footprint length up to 300"
    );

    rig.Generator(SeamRig.Attributes(run: 300), 5);

    Assert.Contains(rig.World.Log.Warnings, w => w.Contains("cut to 240"));
  }

  // Fails when the range rounds down: a reach just over a chunk multiple loses its last chunk.
  [Theory]
  [InlineData(96f, 3)]
  [InlineData(96.5f, 4)]
  [InlineData(144.2f, 5)]
  public void The_chunk_range_covers_the_reach(float radius, int range)
  {
    Assert.Equal(range, SeamDepositGenerator.ChunkRangeFor(radius));
  }

  // Fails when the empty-host warning is removed.
  [Fact]
  public void A_host_code_matching_no_block_warns_and_writes_nothing()
  {
    using var rig = new SeamRig();
    rig.World.Log.Expect(EnumLogType.Warning, "will never spawn");
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      inBlock: """{ "code": "game:rock-*", "name": "rock", "allowedVariants": ["marble"] }"""
    );
    var chunks = rig.Chunks();

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.Equal(0, SeamRig.Count(chunks, rig.Ore.BlockId));
  }

  // Fails when a missing block is logged once per host instead of once per code.
  [Fact]
  public void A_layer_block_that_resolves_for_no_host_warns_once_and_leaves_the_rock()
  {
    using var rig = new SeamRig();
    rig.World.Log.Expect(EnumLogType.Warning, "test:missing");
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      layers: """[ { "block": "test:missing", "thickness": { "dist": "uniform", "avg": 3, "var": 0 } } ]"""
    );
    var chunks = rig.Chunks();
    var before = SeamRig.Column(chunks, 16, 16);

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.Single(rig.World.Log.Warnings, w => w.Contains("test:missing"));
    Assert.Equal(before, SeamRig.Column(chunks, 16, 16));
  }

  // Fails when the handbook attributes are not written.
  [Fact]
  public void Handbook_attributes_pair_the_host_and_the_layer_block()
  {
    using var rig = new SeamRig();

    rig.Generator(SeamRig.Attributes(), 5, v => v.addHandbookAttributes = true);

    Assert.Contains(
      rig.Ore.BlockId,
      rig.Claystone.Attributes!["hostRockFor"].AsArray<int>()!
    );
    Assert.Contains(
      rig.Claystone.BlockId,
      rig.Ore.Attributes!["hostRock"].AsArray<int>()!
    );
  }

  // Fails when minCover is not handed to the stack: the seam reaches the rock top.
  [Fact]
  public void Min_cover_keeps_rock_above_every_layer_block()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      dip: 0,
      anchorY: SeamRig.RockTop - 8,
      minCover: 4,
      layers: """[ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 10, "var": 0 } } ]"""
    );
    var chunks = rig.Chunks();

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    var ys = Enumerable
      .Range(0, 256)
      .Where(y => SeamRig.Id(chunks, 16, y, 16) == rig.Ore.BlockId)
      .ToList();
    Assert.Equal(Enumerable.Range(SeamRig.RockTop - 8, 5).ToList(), ys);
  }

  // Fails when the air test is removed: a sign overwrites the block above the surface.
  [Fact]
  public void A_sign_goes_on_free_air_above_the_surface_in_the_replaced_hosts_variant()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      dip: 0,
      anchorY: SeamRig.RockTop - 2,
      layers: """[ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 3, "var": 0 }, "surfaceBlock": "test:loose-{rock}", "surfaceBlockChance": 1 } ]"""
    );
    var chunks = rig.Chunks(
      hostAt: (lx, _) => lx < 16 ? rig.Claystone.BlockId : rig.Shale.BlockId
    );
    for (int lx = 0; lx < 8; lx++)
    {
      for (int lz = 0; lz < 32; lz++)
      {
        SeamRig.Set(chunks, lx, SeamRig.RockTop + 1, lz, rig.Claystone.BlockId);
      }
    }

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    int above = SeamRig.RockTop + 1;
    for (int lx = 0; lx < 32; lx++)
    {
      int expected =
        lx < 8 ? rig.Claystone.BlockId
        : lx < 16 ? rig.SignClaystone.BlockId
        : rig.SignShale.BlockId;
      Assert.Equal(
        rig.Ore.BlockId,
        SeamRig.Id(chunks, lx, SeamRig.RockTop, 16)
      );
      Assert.Equal(expected, SeamRig.Id(chunks, lx, above, 16));
    }
  }

  // Fails when the sign reads the rain height map: a canopy over the column lifts the sign's row into air.
  [Fact]
  public void A_sign_lands_at_the_terrain_height_under_a_canopy()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      dip: 0,
      anchorY: SeamRig.RockTop - 2,
      layers: """[ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 3, "var": 0 }, "surfaceBlock": "test:loose-{rock}", "surfaceBlockChance": 1 } ]"""
    );
    var chunks = rig.Chunks(rainTop: SeamRig.RockTop + 12);

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.Equal(
      rig.SignClaystone.BlockId,
      SeamRig.Id(chunks, 16, SeamRig.RockTop + 1, 16)
    );
  }

  // Fails when the variant's block callback flag is ignored: the blocks are written, not handed on.
  [Fact]
  public void The_block_callback_flag_hands_every_block_to_the_block()
  {
    using var rig = new SeamRig();
    var ore = new RecordingBlock();
    rig.Add(TestBlocksFor(ore), EnumBlockMaterial.Ore);
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      layers: RecordedLayer
    );
    var chunks = rig.Chunks();

    rig.Generate(
      rig.Generator(attrs, 5, v => v.WithBlockCallback = true),
      chunks,
      0,
      new BlockPos(16, 100, 16)
    );

    Assert.True(ore.Placed.Count > 100);
    Assert.Equal(0, SeamRig.Count(chunks, ore.BlockId));
  }

  // Fails when the last-layer flag is ignored or applied to every block.
  [Fact]
  public void The_last_layer_flag_hands_only_the_lowest_block_of_each_column_to_the_block()
  {
    using var rig = new SeamRig();
    var ore = new RecordingBlock();
    rig.Add(TestBlocksFor(ore), EnumBlockMaterial.Ore);
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      dip: 0,
      anchorY: SeamRig.RockTop - 2,
      layers: RecordedLayer,
      extra: """, "withLastLayerBlockCallback": true"""
    );
    var chunks = rig.Chunks();

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.True(ore.Placed.Count > 100);
    Assert.All(ore.Placed, p => Assert.Equal(SeamRig.RockTop - 2, p.Y));
    Assert.Equal(ore.Placed.Count * 2, SeamRig.Count(chunks, ore.BlockId));
  }

  // Fails when the floor or the ceiling bound is removed: the stack reads a block outside the column.
  [Fact]
  public void A_stack_crossing_the_floor_and_the_ceiling_writes_only_inside_the_world()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      dip: 0,
      anchorY: -2,
      layers: """[ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 270, "var": 0 } } ]"""
    );
    var chunks = rig.Chunks();

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    var ys = Enumerable
      .Range(0, 256)
      .Where(y => SeamRig.Id(chunks, 16, y, 16) == rig.Ore.BlockId)
      .ToList();
    Assert.Equal(Enumerable.Range(2, SeamRig.RockTop - 1).ToList(), ys);
    Assert.Equal(rig.Claystone.BlockId, SeamRig.Id(chunks, 16, 1, 16));
  }

  // Fails when the ceiling guard is removed: a sign reads the cell above the top of the world.
  [Fact]
  public void A_surface_at_the_top_of_the_world_gets_no_sign()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 60,
      strike: 40,
      dip: 0,
      anchorY: 250,
      layers: """[ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 3, "var": 0 }, "surfaceBlock": "test:loose-{rock}", "surfaceBlockChance": 1 } ]"""
    );
    var chunks = rig.Chunks(top: 255);

    rig.Generate(rig.Generator(attrs, 5), chunks, 0, new BlockPos(16, 100, 16));

    Assert.Equal(0, SeamRig.Count(chunks, rig.SignClaystone.BlockId));
    Assert.True(SeamRig.Count(chunks, rig.Ore.BlockId) > 0);
  }

  private const string RecordedLayer =
    """[ { "block": "test:recorded", "thickness": { "dist": "uniform", "avg": 3, "var": 0 } } ]""";

  private static RecordingBlock TestBlocksFor(RecordingBlock block)
  {
    ExpandedLib.Testing.TestBlocks.Configure(block, "test:recorded", 9);
    return block;
  }
}
#endif
