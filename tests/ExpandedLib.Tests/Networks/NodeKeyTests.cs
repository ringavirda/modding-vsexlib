using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Exercises <see cref="NodeKey"/>, the key index the graph keeps beside <see cref="BlockNetwork.Nodes"/>,
/// the default <see cref="INetworkMember.Slots"/> and <see cref="INetworkMember.Crossings"/>, and the
/// opt-in partial split.
/// </summary>
public class NodeKeyTests {
  private sealed class SlottedMember : BEBehaviorNetworkMember, INetworkMember {
    private readonly byte[] _slots;

    public SlottedMember(BlockEntity be, string networkType, byte[] slots)
      : base(be) {
      NetworkType = networkType;
      _slots = slots;
    }

    public IReadOnlyList<byte> Slots(IBlockAccessor world, BlockPos pos) =>
      _slots;
  }

  private sealed class PartialNetwork(BlockNetworkModSystem system)
    : BlockNetwork(system) {
    public List<IReadOnlySet<NodeKey>> Fragments { get; } = [];
    public bool LegacyCalled { get; private set; }

    public override string NetworkType => "partial";
    public override bool SupportsPartialSplit => true;

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) => LegacyCalled = true;

    public override void OnSplitFragment(
      BlockNetwork original,
      IReadOnlySet<NodeKey> fragmentKeys
    ) => Fragments.Add(fragmentKeys.ToHashSet());

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) { }
  }

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    w.RegisterNetwork("partial", sys => new PartialNetwork(sys));
    return w;
  }

  private static void PlaceSlotted(
    TestWorld w,
    BlockPos pos,
    string networkType,
    params byte[] slots
  ) {
    TestMemberBlockEntity be = TestMemberBlockEntity.Carrying();
    be.Behaviors.Add(new SlottedMember(be, networkType, slots));
    w.Place(pos, TestNetworkBlock.Create(networkType, "ns", 1), be);
    w.Initialize(be);
  }

  // Fails when a face piece encodes as the face index without the 1 offset, or its lane is dropped from the slot.
  [Fact]
  public void Slot_LaneIsTrack_encodes_face_and_lane() {
    var pos = new BlockPos(0, 0, 0);
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      NodeKey key = NodeKey.OnFace(pos, face);
      Assert.Equal(1 + face.Index, key.Slot);
      Assert.Equal(face, key.Face);
      Assert.Equal(0, key.Lane);
    }

    NodeKey lane2 = NodeKey.OnFace(pos, BlockFacing.UP, 2);
    Assert.Equal((2 << 3) | 5, lane2.Slot);
    Assert.Equal(BlockFacing.UP, lane2.Face);
    Assert.Equal(2, lane2.Lane);
  }

  // Fails when a bend's far arm crosses at the lane's straight offset instead of the radius from the far corner.
  [Fact]
  public void Slot_LaneIsTrack_bend_offsets_follow_the_radius() {
    Assert.Equal((8, 8), NodeKey.BendOffsets(0));
    Assert.Equal((4, 12), NodeKey.BendOffsets(1));
    Assert.Equal((12, 4), NodeKey.BendOffsets(2));
    Assert.Equal(
      new[] { 8, 4, 12 },
      new[] { 0, 1, 2 }.Select(NodeKey.LaneOffset)
    );
  }

  // Fails when Claim leaves a position's centre key out of NodeKeys.
  [Fact]
  public void An_unslotted_network_keys_each_node_at_slot_0() {
    var w = NewWorld();
    var a = new BlockPos(0, 0, 0);
    var b = new BlockPos(0, 0, 1);
    w.PlaceNode(a, "test", "ns");
    w.PlaceNode(b, "test", "ns");

    BlockNetwork net = w.NetworkAt(a)!;

    Assert.Equal(
      [NodeKey.Centre(a), NodeKey.Centre(b)],
      net.NodeKeys.OrderBy(k => k.Pos.Z)
    );
    Assert.Same(net, w.Networks.GetNetworkAt(NodeKey.Centre(b)));
  }

  // Fails when Slots of a member is ignored and its position is indexed at slot 0 only.
  [Fact]
  public void A_member_with_slots_holds_one_key_per_slot() {
    var w = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    NodeKey up = NodeKey.OnFace(pos, BlockFacing.UP);
    NodeKey port = NodeKey.Port(pos, 3);
    PlaceSlotted(w, pos, "test", 0, up.Slot, port.Slot);

    BlockNetwork net = w.NetworkAt(pos)!;

    Assert.Single(net.Nodes);
    Assert.Equal(3, net.NodeKeys.Count);
    Assert.Same(net, w.Networks.GetNetworkAt(up));
    Assert.Same(net, w.Networks.GetNetworkAt(port));
  }

  // Fails when a removed position's keys stay in the graph's key index.
  [Fact]
  public void Removing_a_node_drops_its_keys() {
    var w = NewWorld();
    var a = new BlockPos(0, 0, 0);
    var b = new BlockPos(0, 0, 1);
    NodeKey aUp = NodeKey.OnFace(a, BlockFacing.UP);
    PlaceSlotted(w, a, "test", 0, aUp.Slot);
    w.PlaceNode(b, "test", "ns");
    BlockNetwork net = w.NetworkAt(b)!;

    w.RemoveNode(a);

    Assert.Null(w.Networks.GetNetworkAt(aUp));
    Assert.Null(w.Networks.GetNetworkAt(NodeKey.Centre(a)));
    Assert.DoesNotContain(aUp, net.NodeKeys);
    Assert.Equal([NodeKey.Centre(b)], w.NetworkAt(b)!.NodeKeys);
  }

  // Fails when a merge copies positions into the surviving network without their slot keys.
  [Fact]
  public void A_merge_carries_the_slot_keys() {
    var w = NewWorld();
    var a = new BlockPos(0, 0, 0);
    var mid = new BlockPos(0, 0, 1);
    var c = new BlockPos(0, 0, 2);
    NodeKey cUp = NodeKey.OnFace(c, BlockFacing.UP);
    w.PlaceNode(a, "test", "ns");
    PlaceSlotted(w, c, "test", 0, cUp.Slot);
    Assert.NotSame(w.NetworkAt(a), w.NetworkAt(c));

    w.PlaceNode(mid, "test", "ns");

    BlockNetwork net = w.NetworkAt(a)!;
    Assert.Same(net, w.NetworkAt(c));
    Assert.Same(net, w.Networks.GetNetworkAt(cUp));
    Assert.Equal(4, net.NodeKeys.Count);
  }

  // Fails when a fracture claims the walk's start cell or a cell it reaches without that cell's slot keys.
  [Fact]
  public void A_fracture_splits_the_keys_by_fragment() {
    var w = NewWorld();
    var a = new BlockPos(0, 0, 0);
    var b = new BlockPos(0, 0, 1);
    var cut = new BlockPos(0, 0, 2);
    var d = new BlockPos(0, 0, 3);
    NodeKey aUp = NodeKey.OnFace(a, BlockFacing.UP);
    NodeKey bUp = NodeKey.OnFace(b, BlockFacing.UP);
    PlaceSlotted(w, a, "test", 0, aUp.Slot);
    PlaceSlotted(w, b, "test", 0, bUp.Slot);
    w.PlaceNode(cut, "test", "ns");
    w.PlaceNode(d, "test", "ns");

    w.RemoveNode(cut);

    BlockNetwork left = w.NetworkAt(a)!;
    BlockNetwork right = w.NetworkAt(d)!;
    Assert.Equal(
      [NodeKey.Centre(a), aUp, NodeKey.Centre(b), bUp],
      left.NodeKeys.OrderBy(k => k.Pos.Z).ThenBy(k => k.Slot)
    );
    Assert.Equal([NodeKey.Centre(d)], right.NodeKeys);
    Assert.Same(left, w.Networks.GetNetworkAt(aUp));
    Assert.Same(left, w.Networks.GetNetworkAt(bUp));
  }

  // Fails when a fracture ignores SupportsPartialSplit and calls the proportional overload.
  [Fact]
  public void A_partial_type_is_split_by_its_fragment_keys() {
    var w = NewWorld();
    var positions = new[]
    {
      new BlockPos(0, 0, 0),
      new BlockPos(0, 0, 1),
      new BlockPos(0, 0, 2),
    };
    foreach (BlockPos p in positions)
      w.PlaceNode(p, "partial", "ns");

    w.RemoveNode(positions[1]);

    var left = (PartialNetwork)w.NetworkAt(positions[0])!;
    var right = (PartialNetwork)w.NetworkAt(positions[2])!;
    Assert.False(left.LegacyCalled);
    Assert.False(right.LegacyCalled);
    Assert.Equal([NodeKey.Centre(positions[0])], Assert.Single(left.Fragments));
    Assert.Equal(
      [NodeKey.Centre(positions[2])],
      Assert.Single(right.Fragments)
    );
  }

  // Fails when a type that does not opt in is split through the key overload, which throws for it.
  [Fact]
  public void A_type_that_does_not_opt_in_keeps_the_proportional_split() {
    var w = NewWorld();
    var a = new BlockPos(0, 0, 0);
    var mid = new BlockPos(0, 0, 1);
    var c = new BlockPos(0, 0, 2);
    foreach (BlockPos p in new[] { a, mid, c })
      w.PlaceNode(p, "test", "ns");
    ((StubNetwork)w.NetworkAt(a)!).Tag = "kept";

    w.RemoveNode(mid);

    Assert.False(w.NetworkAt(a)!.SupportsPartialSplit);
    Assert.Equal("kept", ((StubNetwork)w.NetworkAt(a)!).Tag);
    Assert.Equal("kept", ((StubNetwork)w.NetworkAt(c)!).Tag);
  }

  // Fails when the default Slots is anything but the centre slot.
  [Fact]
  public void The_default_slots_are_the_centre_alone() {
    var w = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    w.PlaceNode(pos, "test", "ns");
    var member = (INetworkMember)
      w.GetBlockEntity(pos)!
        .Behaviors.OfType<BEBehaviorNetworkMember>()
        .Single();

    Assert.Equal([(byte)0], member.Slots(w.Accessor, pos));
  }

  // Fails when the default Crossings answers for a face slot, or lists a face with no connector.
  [Fact]
  public void The_default_crossings_are_the_connector_faces_of_the_centre() {
    var w = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    w.PlaceNode(pos, "test", "ns");
    var member = (INetworkMember)
      w.GetBlockEntity(pos)!
        .Behaviors.OfType<BEBehaviorNetworkMember>()
        .Single();

    var centre = member.Crossings(w.Accessor, NodeKey.Centre(pos)).ToList();
    var onFace = member.Crossings(
      w.Accessor,
      NodeKey.OnFace(pos, BlockFacing.NORTH)
    );

    Assert.Equal(
      [BlockFacing.NORTH, BlockFacing.SOUTH],
      centre.Select(c => c.Face).OrderBy(f => f.Index)
    );
    Assert.All(centre, c => Assert.Null(c.Edge));
    Assert.Empty(onFace);
  }
}
