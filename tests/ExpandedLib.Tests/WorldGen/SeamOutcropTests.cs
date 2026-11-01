#if GAME_GE_1_22
using System;
using System.Linq;
using ExpandedLib.WorldGen;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamOutcropTests
{
  private const int Row = 16;
  private const int PosY = 150;

  private const string OutcropLayer = """
    [ { "block": "test:ore", "thickness": { "dist": "uniform", "avg": 3, "var": 0 },
        "outcrop": { "block": "game:ore-{grade}-limonite-{rock}", "grades": ["poor", "medium"],
                     "depth": { "dist": "uniform", "avg": 3, "var": 0 } } } ]
    """;

  private static string Attributes(float dip = 0.3f) =>
    SeamRig.Attributes(
      run: 100,
      strike: 60,
      dip: dip,
      anchorY: SeamRig.RockTop + 13.15f,
      layers: OutcropLayer,
      inBlock: ClaystoneOnly
    );

  private const string ClaystoneOnly =
    """{ "code": "game:rock-*", "name": "rock", "allowedVariants": ["claystone"] }""";

  private static int TowardEast(SeamBody body) =>
    Math.Abs(body.DipDirection) < 0.05f || body.DipDirection > MathF.Tau - 0.05f
      ? 1
      : 0;

  private static IBlockAccessor ClimateAt(float temp, float rainRel)
  {
    int tempByte = Enumerable
      .Range(0, 256)
      .MinBy(b =>
        Math.Abs(
          Climate.GetScaledAdjustedTemperatureFloat(b, PosY - SeamRig.SeaLevel)
            - temp
        )
      );
    int rainByte = Enumerable
      .Range(0, 256)
      .MinBy(b => Math.Abs(Climate.GetRainFall(b, PosY) / 255f - rainRel));
    int color = (tempByte << 16) | (rainByte << 8);
    var map = new IntDataMap2D
    {
      Data = Enumerable.Repeat(color, 16).ToArray(),
      Size = 4,
    };
    var region = Substitute.For<IMapRegion>();
    region.ClimateMap.Returns(map);
    var accessor = Substitute.For<IBlockAccessor>();
    accessor.GetMapRegion(0, 0).Returns(region);
    return accessor;
  }

  private static (bool Limonite, bool Ore)[] RowAt(
    string attrs,
    IBlockAccessor climate,
    int seed,
    SeamRig rig
  )
  {
    var chunks = rig.Chunks();
    rig.Generate(
      rig.Generator(attrs, seed),
      chunks,
      0,
      new BlockPos(16, PosY, 16),
      climate
    );
    return Enumerable
      .Range(0, 32)
      .Select(x =>
      {
        var column = SeamRig.Column(chunks, x, Row);
        return (
          column.Contains(rig.Poor.BlockId)
            || column.Contains(rig.Medium.BlockId),
          column.Contains(rig.Ore.BlockId)
        );
      })
      .ToArray();
  }

  private static int EastSeed(string attrs, int grade = -1) =>
    SeamRig.SeedWhere(
      attrs,
      b =>
        TowardEast(b) == 1
        && (grade < 0 || PositionHash.Index(b.Seed, 0, 2) == grade)
    );

  // Fails when the climate scale is fixed at 1: the hot and dry strip is 3 columns, the cold wet one is 3.
  // Fails when a region with no climate map reads as scale 0.
  [Fact]
  public void The_strip_runs_depth_down_the_dip_scaled_by_the_climate()
  {
    using var rig = new SeamRig();
    string attrs = Attributes();
    int seed = EastSeed(attrs);

    var temperate = RowAt(attrs, ClimateAt(10, 0.5f), seed, rig);
    var hot = RowAt(attrs, ClimateAt(40, 0), seed, rig);
    var cold = RowAt(attrs, ClimateAt(-20, 1), seed, rig);
    var unmapped = RowAt(attrs, Substitute.For<IBlockAccessor>(), seed, rig);

    for (int x = 17; x < 32; x++)
    {
      Assert.Equal((x <= 19, x >= 20), temperate[x]);
      Assert.Equal((x <= 21, x >= 22), hot[x]);
      Assert.Equal((false, true), cold[x]);
      Assert.Equal(temperate[x], unmapped[x]);
    }
  }

  // Fails when the strip is written at dip 0, where no layer runs down to a rock top.
  [Fact]
  public void A_flat_seam_has_no_strip()
  {
    using var rig = new SeamRig();
    string attrs = SeamRig.Attributes(
      run: 100,
      strike: 60,
      dip: 0,
      anchorY: SeamRig.RockTop - 2,
      layers: OutcropLayer,
      inBlock: ClaystoneOnly
    );

    var row = RowAt(attrs, ClimateAt(40, 0), 5, rig);

    Assert.All(row.Skip(8).Take(16), cell => Assert.Equal((false, true), cell));
  }

  // Fails when {grade} is always the first listed grade: the second body's strip is poor.
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public void The_strip_takes_the_grade_the_body_hash_picks(int grade)
  {
    using var rig = new SeamRig();
    string attrs = Attributes();
    int seed = EastSeed(attrs, grade);
    var chunks = rig.Chunks();

    rig.Generate(
      rig.Generator(attrs, seed),
      chunks,
      0,
      new BlockPos(16, PosY, 16),
      ClimateAt(10, 0.5f)
    );

    int expected = grade == 0 ? rig.Poor.BlockId : rig.Medium.BlockId;
    int other = grade == 0 ? rig.Medium.BlockId : rig.Poor.BlockId;
    var column = SeamRig.Column(chunks, 18, Row);
    Assert.Contains(expected, column);
    Assert.DoesNotContain(other, column);
  }

  // Fails when a strip block that resolves to nothing is not replaced by the layer block.
  [Fact]
  public void A_grade_with_no_block_keeps_the_layer_block_in_the_strip()
  {
    using var rig = new SeamRig();
    rig.World.Log.Expect(EnumLogType.Warning, "ore-rich-limonite");
    string attrs = Attributes().Replace("\"poor\", \"medium\"", "\"rich\"");
    int seed = EastSeed(attrs);

    var row = RowAt(attrs, ClimateAt(10, 0.5f), seed, rig);

    for (int x = 17; x < 32; x++)
    {
      Assert.Equal((false, true), row[x]);
    }
  }
}
#endif
