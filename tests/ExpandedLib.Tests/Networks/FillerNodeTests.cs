using System.Reflection;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A mega-block footprint cell as a graph node: a filler is a plain <see cref="Block"/> that joins
/// through a membership on its block entity, not through the block type itself.
/// </summary>
public class FillerNodeTests {
  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    w.RegisterNetwork("molten", sys => new StubNetwork(sys, "molten"));
    return w;
  }

  #region The retired invariant

  [Fact]
  public void A_filler_cell_bridges_two_nodes_on_opposite_sides_of_itself() {
    var w = NewWorld();
    w.PlaceNode(new BlockPos(0, 0, 0), "test", "ns");
    w.PlaceFillerNode(new BlockPos(0, 0, 1), "test", "ns");
    w.PlaceNode(new BlockPos(0, 0, 2), "test", "ns");
    // The middle block is not a network block; only its membership carries the run.
    Assert.IsNotAssignableFrom<BlockNetworkNode>(
      w.GetBlock(new BlockPos(0, 0, 1))
    );

    var net = w.NetworkAt(new BlockPos(0, 0, 0));

    Assert.NotNull(net);
    Assert.Equal(3, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(new BlockPos(0, 0, 2)));
  }

  [Fact]
  public void The_walk_reaches_a_filler_cell_from_both_directions() {
    var w = NewWorld();
    var node = new BlockPos(0, 0, 0);
    var cell = new BlockPos(0, 0, 1);
    w.PlaceNode(node, "test", "ns");
    w.PlaceFillerNode(cell, "test", "ns");

    Assert.Contains(
      cell,
      w.Networks.GetConnectedNeighbors(w.Accessor, node, "test")
    );
    Assert.Contains(
      node,
      w.Networks.GetConnectedNeighbors(w.Accessor, cell, "test")
    );
  }

  [Fact]
  public void A_filler_cell_bridges_whichever_order_its_neighbours_arrive_in() {
    var w = NewWorld();
    w.PlaceFillerNode(new BlockPos(0, 0, 1), "test", "ns");
    w.PlaceNode(new BlockPos(0, 0, 2), "test", "ns");
    w.PlaceNode(new BlockPos(0, 0, 0), "test", "ns");

    var net = w.NetworkAt(new BlockPos(0, 0, 2));

    Assert.NotNull(net);
    Assert.Equal(3, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(new BlockPos(0, 0, 0)));
  }

  [Fact]
  public void A_filler_cell_with_no_membership_is_still_not_a_node() {
    // A plain footprint cell with no membership is still empty space to the graph.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceNode(new BlockPos(0, 0, 0), "test", "ns");
    w.PlaceFiller(cell);
    w.PlaceNode(new BlockPos(0, 0, 2), "test", "ns");

    Assert.Null(NetworkMembership.Resolve(w.Accessor, cell, "test"));
    Assert.Null(w.NetworkAt(cell));
    Assert.Single(w.NetworkAt(new BlockPos(0, 0, 0))!.Nodes);
    Assert.NotSame(
      w.NetworkAt(new BlockPos(0, 0, 0)),
      w.NetworkAt(new BlockPos(0, 0, 2))
    );
  }

  [Fact]
  public void A_filler_cell_couples_only_on_the_face_it_was_given() {
    // A single-face port must not open its opposite face.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceFillerNode(cell, "test", "n");

    INetworkMember member = Assert.IsAssignableFrom<INetworkMember>(
      NetworkMembership.Resolve(w.Accessor, cell, "test")
    );

    Assert.True(member.HasConnectorAt(w.Accessor, cell, BlockFacing.NORTH));
    Assert.False(member.HasConnectorAt(w.Accessor, cell, BlockFacing.SOUTH));
  }

  #endregion

  #region Membership and port on one cell

  [Fact]
  public void A_membership_answers_for_a_cell_that_also_carries_a_port() {
    // The membership wins over the port when both claim the same cell.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceFillerNode(cell, "test", "ns");
    var be = (BlockEntityStructureFiller)w.GetBlockEntity(cell)!;
    be.PortFace = "e";
    be.PortNetworkType = "test";
    Assert.True(
      ((INetworkMember)w.GetBlock(cell)).HasConnectorAt(
        w.Accessor,
        cell,
        BlockFacing.EAST
      )
    );

    INetworkMember? member = NetworkMembership.Resolve(
      w.Accessor,
      cell,
      "test"
    );

    Assert.IsAssignableFrom<BEBehaviorNetworkMember>(member);
    Assert.True(member!.HasConnectorAt(w.Accessor, cell, BlockFacing.NORTH));
    Assert.False(member.HasConnectorAt(w.Accessor, cell, BlockFacing.EAST));
  }

  [Fact]
  public void A_port_still_answers_for_a_network_no_membership_claims() {
    // A port is a face another network couples to, on a cell that is not a graph member.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceFillerNode(cell, "test", "n");
    var be = (BlockEntityStructureFiller)w.GetBlockEntity(cell)!;
    be.PortFace = "e";
    be.PortNetworkType = "molten";

    Assert.IsAssignableFrom<BEBehaviorNetworkMember>(
      NetworkMembership.Resolve(w.Accessor, cell, "test")
    );
    Assert.IsAssignableFrom<BlockStructureFiller>(
      NetworkMembership.Resolve(w.Accessor, cell, "molten")
    );
  }

  [Fact]
  public void A_membership_stating_no_faces_couples_on_the_cells_port_face() {
    // A declaration with no face of its own inherits the cell's port face.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceNode(new BlockPos(0, 0, 0), "test", "ns");
    PlacePortCell(w, cell, "n", "test", Declaring(null));

    Assert.Equal(2, w.NetworkAt(cell)?.Nodes.Count);
    Assert.Same(w.NetworkAt(cell), w.NetworkAt(new BlockPos(0, 0, 0)));
  }

  #endregion

  #region Lifecycle

  [Fact]
  public void Detaching_a_hosted_membership_drops_its_graph_node() {
    // Re-declaring a cell's behaviours detaches the previous set.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceFillerNode(cell, "test", "n");
    Assert.NotNull(w.NetworkAt(cell));

    ((BlockEntityStructureFiller)w.GetBlockEntity(cell)!).SetHostedBehaviors(
      null
    );

    Assert.Empty(NetworkMembership.MembersOf(w.GetBlockEntity(cell)));
    Assert.Null(w.NetworkAt(cell));
  }

  [Fact]
  public void A_reloaded_filler_cell_keeps_the_node_its_chunk_left_behind() {
    // A hosted behaviour's saved state never replays on the server; the declaration lives on the
    // filler block entity, which does round-trip, and the membership re-registers from it.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceNode(new BlockPos(0, 0, 0), "test", "ns");
    w.PlaceFillerNode(cell, "test", "ns");
    w.PlaceNode(new BlockPos(0, 0, 2), "test", "ns");
    BlockNetwork before = w.NetworkAt(cell)!;
    Assert.Equal(3, before.Nodes.Count);

    w.Reload(cell);

    Assert.Same(before, w.NetworkAt(cell));
    Assert.Equal(3, w.NetworkAt(cell)?.Nodes.Count);
    Assert.Single(w.Networks.AllNetworks);
  }

  [Fact]
  public void A_filler_cell_on_the_client_joins_no_graph() {
    // Registration is server-only.
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.Api.Side.Returns(EnumAppSide.Client);

    w.PlaceFillerNode(cell, "test", "ns");

    Assert.Single(NetworkMembership.MembersOf(w.GetBlockEntity(cell)));
    Assert.Null(w.NetworkAt(cell));
    Assert.Empty(w.Networks.AllNetworks);
  }

  [Fact]
  public void Breaking_a_filler_cell_takes_its_node_with_it() {
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);
    w.PlaceFillerNode(cell, "test", "n");
    Assert.NotNull(w.NetworkAt(cell));

    w.GetBlockEntity(cell)!.OnBlockRemoved();

    Assert.Null(w.NetworkAt(cell));
  }

  #endregion

  #region Registration

  [Fact]
  public void The_membership_behaviour_is_registered_as_a_block_entity_behaviour_class() {
    Assembly exlib = typeof(BEBehaviorNetworkMember).Assembly;

    Assert.Contains(
      typeof(BEBehaviorNetworkMember),
      ReflectionScan.GetCandidateTypes(exlib)
    );
    Assert.NotNull(
      typeof(BEBehaviorNetworkMember).GetCustomAttribute<BlockEntityBehaviorRegisterAttribute>()
    );
    Assert.True(
      typeof(BlockEntityBehavior).IsAssignableFrom(
        typeof(BEBehaviorNetworkMember)
      )
    );
    Assert.Equal(
      TestWorld.NetworkMemberClass,
      EntityRegistry.KeyFor("exlib", typeof(BEBehaviorNetworkMember))
    );
  }

  [Fact]
  public void A_declaring_cell_hosts_a_membership_built_by_the_class_registry() {
    var w = NewWorld();
    var cell = new BlockPos(0, 0, 1);

    w.PlaceFillerNode(cell, "test", "n");

    BEBehaviorNetworkMember member = Assert.Single(
      NetworkMembership.MembersOf(w.GetBlockEntity(cell))
    );
    Assert.Equal("test", member.NetworkType);
    Assert.Equal([BlockFacing.NORTH], member.Connectors);
  }

  #endregion

  #region Machine ports

  [Fact]
  public void A_machine_reads_the_network_across_a_face_into_a_filler_cell() {
    var w = NewWorld();
    var machine = new BlockPos(0, 0, 0);
    var cell = new BlockPos(0, 0, 1);
    w.PlaceFillerNode(cell, "test", "ns");

    Assert.Same(
      w.NetworkAt(cell),
      w.Networks.GetConnectedNetworkAcross(
        w.Accessor,
        machine,
        BlockFacing.SOUTH
      )
    );
  }

  [Fact]
  public void A_machine_reads_no_network_across_a_face_the_cell_does_not_couple_on() {
    var w = NewWorld();
    var machine = new BlockPos(0, 0, 0);
    w.PlaceFillerNode(new BlockPos(0, 0, 1), "test", "s");

    Assert.Null(
      w.Networks.GetConnectedNetworkAcross(
        w.Accessor,
        machine,
        BlockFacing.SOUTH
      )
    );
  }

  #endregion

  #region Helpers

  /// <summary>One membership declaration for <c>network type "test"</c> on <paramref name="face"/>.</summary>
  private static FillerBehavior[] Declaring(BlockFacing? face) =>
    [
      new FillerBehavior(
        TestWorld.NetworkMemberClass,
        face,
        new JsonObject(JToken.Parse("{\"networkType\":\"test\"}"))
      ),
    ];

  /// <summary>Places a footprint cell carrying a fixed port and, optionally, hosted behaviours.</summary>
  private static BlockEntityStructureFiller PlacePortCell(
    TestWorld w,
    BlockPos pos,
    string portFace,
    string portNetworkType,
    FillerBehavior[]? hosted
  ) {
    w.RegisterBlockEntityBehaviorFactory(
      TestWorld.NetworkMemberClass,
      be => new BEBehaviorNetworkMember(be)
    );
    var be = new BlockEntityStructureFiller {
      Principal = pos.AddCopy(0, -1, 0),
      PortFace = portFace,
      PortNetworkType = portNetworkType,
      HostedBehaviors = hosted,
    };
    w.Place(pos, w.Filler, be);
    w.Initialize(be);
    return be;
  }

  #endregion
}
