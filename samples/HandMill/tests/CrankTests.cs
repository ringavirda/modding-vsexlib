using ExpandedLib;
using ExpandedLib.Testing;
using HandMill.BlockEntities;
using HandMill.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace HandMill.Tests;

/// <summary>The crank's drive curve and its wind surviving a save round trip.</summary>
public class CrankTests {
  [Fact]
  public void DriveTorque_is_zero_before_winding() {
    var be = new BlockEntityCrank();
    Assert.Equal(0f, be.DriveTorque(0f));
  }

  [Fact]
  public void DriveTorque_equals_CrankTorque_at_rest_after_winding() {
    var be = new BlockEntityCrank();
    be.Wind(10);
    Assert.Equal(HandMillValues.CrankTorque, be.DriveTorque(0f));
  }

  [Fact]
  public void DriveTorque_is_zero_at_the_run_burst_speed() {
    var be = new BlockEntityCrank();
    be.Wind(10);
    Assert.Equal(0f, be.DriveTorque(ExlibValues.MpMaxSpeed));
  }

  [Fact]
  public void The_wind_survives_a_tree_round_trip() {
    var world = new TestWorld();
    Block block = TestBlocks.Configure(new BlockCrank(), "handmill:crank-e", 1, ("orientation", "e"));
    var be = new BlockEntityCrank();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);
    be.Wind(10);

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var restored = new BlockEntityCrank { Pos = be.Pos, Block = be.Block };
    restored.FromTreeAttributes(tree, world.World);

    Assert.Equal(HandMillValues.CrankTorque, restored.DriveTorque(0f));
  }
}
