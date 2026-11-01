#if GAME_GE_1_22
using System.Linq;
using ExpandedLib.WorldGen;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamPropickTests
{
  private const int PosY = 150;

  private static int[] Column(SeamRig rig, int hostsInTop48)
  {
    var column = Enumerable.Repeat(rig.Granite.BlockId, 256).ToArray();
    for (int i = 0; i < hostsInTop48; i++)
    {
      column[PosY - 1 - i] = rig.Claystone.BlockId;
    }
    return column;
  }

  // Fails when the host share is taken over the whole column: 34 of 256 reads 0.133, not 34 of 48.
  [Fact]
  public void The_reading_is_the_host_share_of_the_top_48_blocks_and_the_formula()
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);
    var generator = SeamFixtures.Generator(
      rig,
      SeamFixtures.Variant("ironstone")
    );

    generator.GetPropickReading(
      new BlockPos(0, PosY, 0),
      255,
      Column(rig, 34),
      out double ppt,
      out double totalFactor
    );

    Assert.Equal(34 / 48.0, totalFactor, 9);
    double quantity =
      DepositFootprint.Area(160, 96, 4)
      * 3
      * DepositFootprint.ExpectedFactor(4, 0.6f, 1.0f)
      * 0.0076;
    Assert.Equal(totalFactor * quantity / (PosY * 1024.0) * 1000, ppt, 6);
    Assert.True(ppt > 0);
  }

  // Fails when the world scale is not applied to the reading: the off world reads like a normal one.
  [Fact]
  public void An_off_world_reads_nothing()
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);
    rig.World.Config.Tree.SetString("iiexBulkOre", "off");
    var generator = SeamFixtures.Generator(
      rig,
      SeamFixtures.Variant("ironstone")
    );

    generator.GetPropickReading(
      new BlockPos(0, PosY, 0),
      255,
      Column(rig, 34),
      out double ppt,
      out double totalFactor
    );

    Assert.Equal(0, totalFactor);
    Assert.Equal(0, ppt);
  }

  // Fails when the dip term is dropped: miny reads 115.5.
  [Fact]
  public void The_sealevel_height_range_runs_from_the_crop_less_the_dip_to_the_crop_plus_the_stack()
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);
    rig.World.World.SeaLevel.Returns(110);
    var generator = SeamFixtures.Generator(
      rig,
      SeamFixtures.Variant("ironstone")
    );

    generator.GetYMinMax(
      new BlockPos(0, PosY, 0),
      out double miny,
      out double maxy
    );

    Assert.Equal(67.5, miny, 2);
    Assert.Equal(133.8, maxy, 2);
  }

  // Fails when the depth bounds are swapped: maxy reads 63.
  [Fact]
  public void The_surface_height_range_hangs_from_the_pick_height()
  {
    using var rig = new SeamRig();
    SeamFixtures.AddBlocks(rig);
    var generator = SeamFixtures.Generator(
      rig,
      SeamFixtures.Variant("hematite-flat")
    );

    generator.GetYMinMax(
      new BlockPos(0, 100, 0),
      out double miny,
      out double maxy
    );

    Assert.Equal(100 - 40 - 0.1 * 64, miny, 2);
    Assert.Equal(100 - 12 + 3, maxy, 2);
  }
}
#endif
