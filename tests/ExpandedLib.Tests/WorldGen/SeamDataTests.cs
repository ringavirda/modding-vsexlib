#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using ExpandedLib.WorldGen;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

/// <summary>The example deposit files through the registry, the world scale and the province
/// chance. Runs alone: the registry and the province table are process-wide.</summary>
[Collection(ExHarmonyCollection.Name)]
public class SeamDataTests
{
  // Fails when the mod system's Start does not register the generator code: the variant gets none.
  [Theory]
  [InlineData("ironstone", 160, 0.3f, 1, 4, 80f)]
  [InlineData("hematite-flat", 64, 0.05f, 1, 1, 32f)]
  [InlineData("coal-measures", 160, 0.3f, 6, 4, 80f)]
  public void A_fixture_builds_its_generator_through_the_registry(
    string name,
    float run,
    float dip,
    int layers,
    int hosts,
    float radius
  )
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);
    var exlib = new ExpandedLibModSystem();
    ReflectionHelpers.SetProperty(
      exlib,
      nameof(ModSystem.Mod),
      rig.World.Mods.GetMod("exlib")!
    );
    try
    {
      exlib.Start(rig.World.Api);
      DepositVariant variant = SeamFixtures.Variant(name);

      variant.Init(rig.World.Api, new LCGRandom(1), rig.Noise);

      var generator = Assert.IsType<SeamDepositGenerator>(
        variant.GeneratorInst
      );
      Assert.Equal(run, generator.Spec!.Footprint.Run.avg);
      Assert.Equal(dip, generator.Spec.Dip.avg);
      Assert.Equal(layers, generator.Spec.Layers.Count);
      Assert.Equal(hosts, generator.HostCount);
      Assert.Equal(radius, generator.GetMaxRadius());
    }
    finally
    {
      exlib.Dispose();
    }
  }

  private static (
    DepositVariant Variant,
    SeamDepositGenerator Generator
  ) Scaled(SeamRig rig, string name, string? setting)
  {
    if (setting != null)
    {
      rig.World.Config.Tree.SetString("iiexBulkOre", setting);
    }
    DepositVariant variant = SeamFixtures.Variant(name);
    return (variant, SeamFixtures.Generator(rig, variant));
  }

  // Fails when the multiplier is ignored: sparse and rich keep 0.0076.
  [Theory]
  [InlineData("sparse", 0.0038f)]
  [InlineData("normal", 0.0076f)]
  [InlineData("rich", 0.0152f)]
  [InlineData(null, 0.0076f)]
  [InlineData("off", 0f)]
  public void The_world_setting_scales_the_tries_per_chunk(
    string? setting,
    float expected
  )
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);

    var (variant, _) = Scaled(rig, "ironstone", setting);

    Assert.Equal(expected, variant.TriesPerChunk, 6);
  }

  // Fails when GenDeposit does not stop at a zero scale: the off world gets an ironstone seam.
  [Theory]
  [InlineData("rich", true)]
  [InlineData("off", false)]
  public void A_world_with_the_setting_off_writes_no_block(
    string setting,
    bool writes
  )
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);
    var (_, generator) = Scaled(rig, "ironstone", setting);
    var chunks = rig.Chunks();

    rig.Generate(generator, chunks, 0, new BlockPos(16, 100, 16));

    int ironstone = rig
      .World.World.GetBlock(new AssetLocation("iiex:seamore-ironstone"))
      .BlockId;
    Assert.Equal(writes, SeamRig.Count(chunks, ironstone) > 0);
  }

  private static GeologicProvinces Provinces() =>
    new()
    {
      Variants =
      [
        new GeologicProvinceVariant { Code = "shield" },
        new GeologicProvinceVariant { Code = "basin" },
      ],
    };

  private static int Measures(SeamRig rig, int province)
  {
    var (_, generator) = Scaled(rig, "coal-measures", null);
    var chunks = rig.Chunks();
    var map = new IntDataMap2D
    {
      Data = new int[64],
      Size = 8,
      TopLeftPadding = 2,
      BottomRightPadding = 2,
    };
    System.Array.Fill(map.Data, province);
    ((IServerChunk)chunks[0]).MapChunk.MapRegion.GeologicProvinceMap.Returns(
      map
    );

    rig.Generate(generator, chunks, 0, new BlockPos(16, 100, 16));

    return SeamRig.Count(
      chunks,
      rig.World.World.GetBlock(
        new AssetLocation("iiex:ore-bituminouscoal")
      ).BlockId
    );
  }

  // Fails when the province chance is ignored: the body centred in a shield writes coal.
  [Fact]
  public void A_body_in_a_shield_writes_nothing_and_in_a_basin_writes()
  {
    var before = NoiseGeoProvince.provinces;
    NoiseGeoProvince.provinces = Provinces();
    try
    {
      using var shield = new SeamRig();
      SeamFixtures.AddBlocks(shield);
      using var basin = new SeamRig();
      SeamFixtures.AddBlocks(basin);

      Assert.Equal(0, Measures(shield, 0));
      Assert.True(Measures(basin, 1) > 0);
    }
    finally
    {
      NoiseGeoProvince.provinces = before;
    }
  }
}
#endif
