#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.WorldGen;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamWorldGenTests
{
  private static DepositVariant Variant(
    string attributes,
    float tries = 1,
    bool oreMap = false
  ) =>
    new()
    {
      fromFile = "test.json",
      Code = "test",
      Generator = SeamDepositGenerator.Code,
      Attributes = new JsonObject(JToken.Parse(attributes)),
      TriesPerChunk = tries,
      ScaleWithWorldheight = false,
      WithOreMap = oreMap,
      HandbookPageCode = "block-test:ore",
    };

  private static SeamWorldGen Pass(
    SeamRig rig,
    DepositVariant variant,
    int seed = 1
  )
  {
    DepositGeneratorRegistry.RegisterDepositGenerator<SeamDepositGenerator>(
      SeamDepositGenerator.Code
    );
    rig.World.Api.WorldManager.Seed.Returns(seed);
    rig.World.Api.WorldManager.MapSizeY.Returns(256);
    var pass = new SeamWorldGen();
    pass.Load(rig.World.Api, [variant], 1);
    pass.initWorldGen();
    return pass;
  }

  private static ProPickWorkSpace.DummyChunk[] Column(
    SeamRig rig,
    SeamWorldGen pass
  )
  {
    var chunks = rig.Chunks();
    var request = Substitute.For<IChunkColumnGenerateRequest>();
    request.Chunks.Returns(chunks);
    request.ChunkX.Returns(0);
    request.ChunkZ.Returns(0);
    pass.Generate(request);
    return chunks;
  }

  private const string Wide =
    """, "worldScale": { "code": "iiexBulkOre", "values": { "off": 0, "normal": 1 } }""";

  // Fails when the pass ignores the seams' reach: a 240 x 160 body stays at three chunks.
  [Fact]
  public void A_long_body_widens_the_pass_range_to_its_reach()
  {
    using var rig = new SeamRig();

    var pass = Pass(rig, Variant(SeamRig.Attributes(run: 160, strike: 240)));

    Assert.Equal(5, pass.ChunkRange);
  }

  // Fails when the off world widens the range: a body that places nothing still costs the wider scan.
  [Fact]
  public void An_off_world_leaves_the_pass_range_at_three()
  {
    using var rig = new SeamRig();
    rig.World.Config.Tree.SetString("iiexBulkOre", "off");

    var pass = Pass(
      rig,
      Variant(SeamRig.Attributes(run: 160, strike: 240, extra: Wide))
    );

    Assert.Equal(3, pass.ChunkRange);
  }

  // Fails when the range is set from the reach rather than floored: a short body lowers it to 1.
  [Fact]
  public void A_short_body_leaves_the_pass_range_at_three()
  {
    using var rig = new SeamRig();

    var pass = Pass(rig, Variant(SeamRig.Attributes(run: 24, strike: 16)));

    Assert.Equal(3, pass.ChunkRange);
  }

  // Fails when Load widens vanilla's pass: GenDeposits would roll 121 source chunks per deposit.
  [Fact]
  public void Vanillas_deposit_range_stays_three_with_seams_registered()
  {
    using var rig = new SeamRig();
    var deposits = new GenDeposits();
    rig.World.Mods.Register(deposits);

    Pass(rig, Variant(SeamRig.Attributes(run: 160, strike: 240)));

    Assert.Equal(3, deposits.depositChunkRange);
  }

  // Fails when the pass runs at or before vanilla's deposits: 0.1 puts seams ahead of their ore.
  [Fact]
  public void The_pass_runs_after_vanillas_deposits()
  {
    Assert.True(
      new SeamWorldGen().ExecuteOrder() > new GenDeposits().ExecuteOrder()
    );
  }

  // Fails at TerrainFeatures 0.21, where vanilla's structures meet seam blocks and skip a site.
  // Fails at 0.96, after vanilla's sunlight.
  [Fact]
  public void The_pass_runs_after_every_vanilla_pass_that_reads_the_column()
  {
    double order = new SeamWorldGen().ExecuteOrder();

    Assert.Equal(EnumWorldGenPass.Vegetation, SeamWorldGen.Pass);
    Assert.True(order > new GenRivulets().ExecuteOrder());
    Assert.True(order < new GenLightSurvival().ExecuteOrder());
  }

  // Fails when the timer is not wrapped around the column: a generated column leaves the seconds at zero.
  // Fails when the column count is not incremented: two columns read as none.
  [Fact]
  public void The_pass_times_the_columns_it_generates()
  {
    using var rig = new SeamRig();
    var pass = Pass(rig, Variant(SeamRig.Attributes(run: 120, strike: 120), 3));
    Assert.Equal(0, pass.ColumnsGenerated);

    Column(rig, pass);
    Column(rig, pass);

    Assert.Equal(2, pass.ColumnsGenerated);
    Assert.True(pass.GenerationSeconds > 0);
  }

  // Fails when the pass seeds from anything but the world seed (a counter, the clock): the second run differs.
  // Fails when the world seed is ignored: seeds 5 and 6 lay the same column.
  [Fact]
  public void The_pass_is_deterministic_per_seed()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(run: 120, strike: 120);

    List<int> Laid(int seed) =>
      SeamRig
        .Column(Column(rig, Pass(rig, Variant(attrs, 3), seed)), 16, 16)
        .ToList();
    int ore = rig.Ore.BlockId;
    var first = Laid(5);
    var again = Laid(5);
    var other = Laid(6);

    Assert.Contains(ore, first);
    Assert.Equal(first, again);
    Assert.NotEqual(first, other);
  }

  // Fails when the ore map layers are not built from the world seed: the region gets no map.
  [Fact]
  public void A_region_gets_the_seams_ore_map()
  {
    using var rig = new SeamRig();
    var pass = Pass(rig, Variant(SeamRig.Attributes(), 1, oreMap: true));
    var region = Substitute.For<IMapRegion>();
    var maps = new Dictionary<string, IntDataMap2D>();
    region.OreMaps.Returns(maps);

    pass.OnMapRegionGen(region, 0, 0);

    Assert.Contains("test", maps.Keys);
  }

  // Fails when the pick workspace is not given the seam: the pick reads no seam deposit.
  // Fails when a seam without an ore map is listed: the pick would look for a map that is never made.
  [Fact]
  public void The_prospecting_pick_lists_the_seams_with_ore_maps()
  {
    using var rig = new SeamRig();
    var mapped = Pass(rig, Variant(SeamRig.Attributes(), 1, oreMap: true));
    var plain = Pass(rig, Variant(SeamRig.Attributes(), 1, oreMap: false));
    var withMap = new ProPickWorkSpace();
    var without = new ProPickWorkSpace();

    mapped.RegisterProspecting(withMap);
    plain.RegisterProspecting(without);
    mapped.RegisterProspecting(null);

    Assert.Same(mapped.Variants[0], withMap.depositsByCode["test"]);
    Assert.Equal("block-test:ore", withMap.pageCodes["test"]);
    Assert.Empty(without.depositsByCode);
  }
}
#endif
