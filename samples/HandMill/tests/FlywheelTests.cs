using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using HandMill.BlockEntities;
using HandMill.Blocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace HandMill.Tests;

/// <summary>The flywheel's footprint, its membership connectors at two
/// orientations, and braking the run it sits on.</summary>
public class FlywheelTests {
  private static JObject Attributes() {
    ExBlockDef def = BlockFlywheel.Definitions("handmill").Single();
    return (JObject)def.ToJson()["attributes"]!;
  }

  private static BlockFlywheel Wheel(string side, int id) {
    var block = TestBlocks.Configure(
      new BlockFlywheel(),
      $"handmill:flywheel-{side}",
      id,
      ("side", side)
    );
    block.Attributes = new JsonObject(Attributes());
    return block;
  }

  [Fact]
  public void The_footprint_reserves_the_eight_rim_cells() {
    BlockFlywheel block = Wheel("n", 1);
    var hub = new BlockPos(0, 0, 0);
    var cells = StructureFillers.FootprintCells(
      block,
      hub,
      block.StructureAngle
    );
    Assert.Equal(8, cells.Count);

    // The ring sits in the vertical XY plane at the hub's own Z, not a
    // horizontal XZ ring, and every X/Y combination but the hub itself is
    // filled.
    var offsets = cells
      .Select(c => (c.Pos.X - hub.X, c.Pos.Y - hub.Y, c.Pos.Z - hub.Z))
      .ToHashSet();
    var expected = new HashSet<(int, int, int)>();
    for (int x = -1; x <= 1; x++)
      for (int y = -1; y <= 1; y++)
        if (x != 0 || y != 0)
          expected.Add((x, y, 0));
    Assert.Equal(expected, offsets);
  }

  [Fact]
  public void At_north_the_membership_couples_north_and_south() {
    TestWorld world = new TestWorld();
    BlockFlywheel block = Wheel("n", 1);
    var be = new BlockEntityFlywheel();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);

    INetworkMember member = NetworkMembership.MemberOf(be, "mpenergy")!;
    Assert.True(
      member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.NORTH)
    );
    Assert.True(
      member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.SOUTH)
    );
    Assert.False(
      member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.EAST)
    );
  }

  [Fact]
  public void At_east_the_membership_couples_east_and_west() {
    TestWorld world = new TestWorld();
    BlockFlywheel block = Wheel("e", 1);
    var be = new BlockEntityFlywheel();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);

    INetworkMember member = NetworkMembership.MemberOf(be, "mpenergy")!;
    Assert.True(
      member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.EAST)
    );
    Assert.True(
      member.HasConnectorAt(world.Accessor, be.Pos, BlockFacing.WEST)
    );
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
    net!.RestoreState(
      new MpEnergyNetworkState {
        Speed = 5f,
        Inertia = 40f,
        StoredEnergy = 500f,
      }
    );

    be.Brake();

    Assert.Equal(0f, net.State!.Speed);
    Assert.Equal(0f, net.State!.StoredEnergy);
  }

  [Fact]
  public void A_sneak_click_on_a_rim_cell_brakes_the_wheel_via_the_filler() {
    var world = new TestWorld().RegisterNetwork(
      "mpenergy",
      sys => new MpEnergyNetwork(sys)
    );
    var hub = new BlockPos(0, 0, 0);
    // OnFillerInteractStart is server-only; the fake world otherwise
    // leaves Side unstubbed, which does not equal EnumAppSide.Server.
    world.World.Side.Returns(EnumAppSide.Server);
    BlockFlywheel block = Wheel("n", 1);
    var be = new BlockEntityFlywheel();
    world.Place(hub, block, be);
    world.Initialize(be);

    MpEnergyNetwork? net = world.NetworkAt(be.Pos) as MpEnergyNetwork;
    net!.RestoreState(
      new MpEnergyNetworkState {
        Speed = 5f,
        Inertia = 40f,
        StoredEnergy = 500f,
      }
    );

    // A rim cell one step off the hub, wired to the principal the way
    // PlaceFillers wires every real footprint cell.
    var rimCell = new BlockPos(1, 0, 0);
    world.PlaceFiller(rimCell, principal: hub);

    TestPlayer player = world.Player();
    player.Entity.Controls.ShiftKey = true;
    var selection = new BlockSelection { Position = rimCell };
    bool handled = world.Filler.OnBlockInteractStart(
      world.World,
      player.Player,
      selection
    );

    Assert.True(handled);
    Assert.Equal(0f, net.State!.Speed);
    Assert.Equal(0f, net.State!.StoredEnergy);
  }

  [Fact]
  public void A_plain_click_on_a_rim_cell_leaves_the_wheel_running() {
    var world = new TestWorld().RegisterNetwork(
      "mpenergy",
      sys => new MpEnergyNetwork(sys)
    );
    var hub = new BlockPos(0, 0, 0);
    BlockFlywheel block = Wheel("n", 1);
    var be = new BlockEntityFlywheel();
    world.Place(hub, block, be);
    world.Initialize(be);

    MpEnergyNetwork? net = world.NetworkAt(be.Pos) as MpEnergyNetwork;
    net!.RestoreState(
      new MpEnergyNetworkState {
        Speed = 5f,
        Inertia = 40f,
        StoredEnergy = 500f,
      }
    );

    var rimCell = new BlockPos(1, 0, 0);
    world.PlaceFiller(rimCell, principal: hub);

    TestPlayer player = world.Player();
    var selection = new BlockSelection { Position = rimCell };
    bool handled = world.Filler.OnBlockInteractStart(
      world.World,
      player.Player,
      selection
    );

    Assert.False(handled);
    Assert.Equal(5f, net.State!.Speed);
  }
}
