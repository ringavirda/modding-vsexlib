using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using HandMill.BlockEntities;
using HandMill.Blocks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace HandMill.Tests;

/// <summary>The flywheel's footprint, its membership connectors at two orientations, and braking the
/// run it sits on.</summary>
public class FlywheelTests {
  private static JObject Attributes() {
    ExBlockDef def = BlockFlywheel.Definitions("handmill").Single();
    return (JObject)def.ToJson()["attributes"]!;
  }

  private static BlockFlywheel Wheel(string side, int id) {
    var block = TestBlocks.Configure(new BlockFlywheel(), $"handmill:flywheel-{side}", id, ("side", side));
    block.Attributes = new JsonObject(Attributes());
    return block;
  }

  [Fact]
  public void The_footprint_reserves_the_eight_rim_cells() {
    BlockFlywheel block = Wheel("n", 1);
    var cells = StructureFillers.FootprintCells(block, new BlockPos(0, 0, 0), block.StructureAngle);
    Assert.Equal(8, cells.Count);
  }

  [Fact]
  public void At_north_the_membership_couples_north_and_south() {
    TestWorld world = new TestWorld();
    BlockFlywheel block = Wheel("n", 1);
    var be = new BlockEntityFlywheel();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);

    INetworkMember member = NetworkMembership.MemberOf(be, "mpenergy")!;
    Assert.True(member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.NORTH));
    Assert.True(member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.SOUTH));
    Assert.False(member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.EAST));
  }

  [Fact]
  public void At_east_the_membership_couples_east_and_west() {
    TestWorld world = new TestWorld();
    BlockFlywheel block = Wheel("e", 1);
    var be = new BlockEntityFlywheel();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);

    INetworkMember member = NetworkMembership.MemberOf(be, "mpenergy")!;
    Assert.True(member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.EAST));
    Assert.True(member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.WEST));
  }

  [Fact]
  public void Brake_zeroes_the_networks_energy() {
    var world = new TestWorld().RegisterNetwork(
      "mpenergy",
      sys => new MpEnergyNetwork(sys)
    );
    BlockFlywheel block = Wheel("n", 1);
    var be = new BlockEntityFlywheel();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);

    MpEnergyNetwork? net = world.NetworkAt(be.Pos) as MpEnergyNetwork;
    net!.RestoreState(new MpEnergyNetworkState { Speed = 5f, Inertia = 40f, StoredEnergy = 500f });

    be.Brake();

    Assert.Equal(0f, net.State!.Speed);
    Assert.Equal(0f, net.State!.StoredEnergy);
  }
}
