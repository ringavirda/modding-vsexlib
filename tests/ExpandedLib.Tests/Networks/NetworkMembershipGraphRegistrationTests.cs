using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using static ExpandedLib.Tests.NetworkMembershipFixtures;

namespace ExpandedLib.Tests;

public class NetworkMembershipGraphRegistrationTests {
  [Fact]
  public void A_membership_registers_its_position_as_a_graph_node() {
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = TestMemberBlockEntity.With(w, pos, "test");

    w.Initialize(be);

    Assert.NotNull(w.NetworkAt(pos));
  }

  [Fact]
  public void Removing_the_block_unregisters_the_membership() {
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = TestMemberBlockEntity.With(w, pos, "test");
    w.Initialize(be);
    Assert.NotNull(w.NetworkAt(pos));

    be.OnBlockRemoved();

    Assert.Null(w.NetworkAt(pos));
  }

  [Fact]
  public void A_chunk_unload_keeps_the_node_and_leaves_the_block_placed() {
    // An unload drops the block entity while the block stays placed.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = TestMemberBlockEntity.With(w, pos, "test");
    w.Initialize(be);
    Block placed = w.GetBlock(pos);
    Assert.NotNull(w.NetworkAt(pos));

    w.Unload(pos);

    Assert.NotNull(w.NetworkAt(pos));
    Assert.Null(w.GetBlockEntity(pos));
    Assert.Same(placed, w.GetBlock(pos));
  }

  [Fact]
  public void An_attached_but_uninitialised_node_still_deregisters_on_removal() {
    // Attach sets only the block entity's own api; the graph node is added by hand without Initialize.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new SeverableNode();
    w.Place(pos, TestNetworkBlock.Create("test", "ns", id: 911), be);
    w.Attach(be);
    ReflectionHelpers.SetProperty(be, nameof(be.NetworkSystem), w.Networks);
    w.AddNode(pos, "test");
    Assert.Null(NetworkMembership.MembersOf(be).Single().Api);
    Assert.NotNull(w.NetworkAt(pos));

    be.OnBlockRemoved();

    Assert.Null(w.NetworkAt(pos));
  }

  [Fact]
  public void A_membership_naming_no_network_joins_nothing_rather_than_throwing() {
    // A blank network type leaves the cell off the graph and logs an error.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = TestMemberBlockEntity.With(w, pos, "");

    w.Initialize(be);

    Assert.Null(w.NetworkAt(pos));
    AssertErrorLogged(w, "names no network type", pos);
  }

  [Fact]
  public void A_declared_network_type_reaches_a_membership_that_names_none() {
    // The declared type reaches the block entity through the hosted membership's forwarding setter.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new SeverableNode { NetworkType = "" };
    w.Place(pos, TestNetworkBlock.Create("molten", "ns", id: 914), be);
    Declare(be, "molten");

    w.Initialize(be);

    Assert.Equal("molten", be.NetworkType);
    Assert.Equal("molten", w.NetworkAt(pos)?.NetworkType);
  }

  [Fact]
  public void A_declared_network_type_loses_to_the_block_entitys_own_and_says_so() {
    // A block entity that already names its network owns that answer; a losing declaration is refused
    // out loud.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new SeverableNode();
    w.Place(pos, TestNetworkBlock.Create("test", "ns", id: 915), be);
    Declare(be, "molten");

    w.Initialize(be);

    Assert.Equal("test", be.NetworkType);
    Assert.Equal("test", w.NetworkAt(pos)?.NetworkType);
    AssertErrorLogged(w, "already names", pos, "molten", "test");
  }

  [Fact]
  public void A_reloaded_block_entity_rejoins_the_node_its_chunk_left_behind() {
    // An unload keeps the node; the returning block entity must find it and skip AddNode.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    w.Place(
      pos,
      TestNetworkBlock.Create("test", "ns", id: 912),
      new TaggedNode()
    );
    w.Initialize(w.GetBlockEntity(pos)!);
    BlockNetwork before = w.NetworkAt(pos)!;

    w.Reload(pos);

    Assert.Same(before, w.NetworkAt(pos));
    Assert.Single(w.Networks.AllNetworks);
  }

  [Fact]
  public void A_hosted_membership_answers_its_block_entitys_type_through_the_interface() {
    // Read as INetworkMember, against a production node, and before Initialize.
    var w = NewGraphWorld();
    w.RegisterNetwork("pipe", sys => new StubNetwork(sys, "pipe"));
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 913), be);

    INetworkMember member = Assert.Single(NetworkMembership.MembersOf(be));

    Assert.Equal("pipe", member.NetworkType);
    Assert.Equal("pipe", member.NetworkTypeAt(w.Accessor, pos));

    w.Initialize(be);

    Assert.Equal("pipe", w.NetworkAt(pos)?.NetworkType);
  }

  [Fact]
  public void A_network_node_registers_under_its_own_network_type() {
    // NetworkType is a plain auto-property the save tree fills via FromTreeAttributes, before Initialize.
    var w = NewGraphWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new SeverableNode();
    w.Place(pos, TestNetworkBlock.Create("molten", "ns", id: 906), be);
    var tree = new TreeAttribute();
    tree.SetString("networkType", "molten");
    be.FromTreeAttributes(tree, w.World);

    w.Initialize(be);

    Assert.Equal("molten", w.NetworkAt(pos)?.NetworkType);
  }

  [Fact]
  public void Saved_network_state_survives_a_reload_next_to_a_live_run() {
    // AddNode broadcasts on join, which clears saved state via OnNetworkUpdate; restore must read
    // before registration.
    var w = NewGraphWorld();
    var block = TestNetworkBlock.Create("test", "ns", id: 907);
    var anchor = new BlockPos(0, 0, 0);
    var reloading = new BlockPos(0, 0, 1);
    w.Place(anchor, block, new TaggedNode());
    w.Place(reloading, block, new TaggedNode());
    w.Initialize(w.GetBlockEntity(anchor)!);
    w.Initialize(w.GetBlockEntity(reloading)!);

    BlockNetwork net = w.NetworkAt(anchor)!;
    net.RestoreState("hot");
    net.BroadcastUpdate(w.Accessor); // both nodes cache the state for the next load
    net.RestoreState(null); // the run itself forgets
    w.RemoveNode(reloading); // this cell's node goes with its chunk

    w.Reload(reloading);

    Assert.Equal("hot", w.NetworkAt(anchor)?.State);
  }

  /// <summary>A network node that persists its last broadcast network state. <c>StubNetwork</c> state
  /// is a plain string.</summary>
  private sealed class TaggedNode : BlockEntityNetworkNode {
    public override string NetworkType { get; set; } = "test";

    protected override object? DeserializeNetworkState(ITreeAttribute tree) =>
      tree.HasAttribute("netTag") ? tree.GetString("netTag") : null;

    protected override void SerializeNetworkState(
      ITreeAttribute tree,
      object? state
    ) {
      if (state is string tag)
        tree.SetString("netTag", tag);
    }
  }
}
