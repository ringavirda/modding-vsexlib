using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="INetworkMember.AcceptsNeighbour"/> asked of both cells of a pair: a node that
/// accepts its neighbour and a neighbour that refuses it stay apart whichever side is placed or walked
/// first.
/// </summary>
public class NeighbourAcceptanceTests {
  private static readonly BlockPos AcceptingPos = new(0, 0, 0);
  private static readonly BlockPos RefusingPos = new(0, 0, 1);

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    return w;
  }

  private static (JointNode accepting, JointNode refusing) PlacePair(
    TestWorld w,
    bool acceptingFirst
  ) {
    var accepting = TestBlocks.Configure(new JointNode(), "test:accepting", 1);
    var refusing = TestBlocks.Configure(
      new JointNode { Refuses = accepting },
      "test:refusing",
      2
    );

    if (acceptingFirst) {
      w.Place(AcceptingPos, accepting);
      w.AddNode(AcceptingPos, "test");
    }
    w.Place(RefusingPos, refusing);
    w.AddNode(RefusingPos, "test");
    if (!acceptingFirst) {
      w.Place(AcceptingPos, accepting);
      w.AddNode(AcceptingPos, "test");
    }
    return (accepting, refusing);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void A_one_sided_refusal_keeps_two_networks_in_either_placement_order(
    bool acceptingFirst
  ) {
    var w = NewWorld();
    PlacePair(w, acceptingFirst);

    var acceptingNet = w.NetworkAt(AcceptingPos);
    var refusingNet = w.NetworkAt(RefusingPos);

    Assert.NotNull(acceptingNet);
    Assert.NotNull(refusingNet);
    Assert.NotSame(acceptingNet, refusingNet);
    Assert.Single(acceptingNet!.Nodes);
    Assert.Single(refusingNet!.Nodes);
  }

  [Fact]
  public void A_one_sided_refusal_holds_whichever_cell_the_walk_starts_from() {
    var w = NewWorld();
    PlacePair(w, acceptingFirst: true);

    Assert.Empty(
      w.Networks.GetConnectedNeighbors(w.Accessor, AcceptingPos, "test")
    );
    Assert.Empty(
      w.Networks.GetConnectedNeighbors(w.Accessor, RefusingPos, "test")
    );
    Assert.Single(
      w.Networks.RebuildFromRoot(w.Accessor, AcceptingPos, "test")!.Nodes
    );
    Assert.Single(
      w.Networks.RebuildFromRoot(w.Accessor, RefusingPos, "test")!.Nodes
    );
  }

  [Fact]
  public void A_one_sided_refusal_leaves_the_face_open_on_both_sides() {
    var w = NewWorld();
    var (accepting, refusing) = PlacePair(w, acceptingFirst: true);

    Assert.Contains(
      BlockFacing.SOUTH,
      w.Networks.GetOpenConnectorFaces(w.Accessor, AcceptingPos, accepting)
    );
    Assert.Contains(
      BlockFacing.NORTH,
      w.Networks.GetOpenConnectorFaces(w.Accessor, RefusingPos, refusing)
    );
  }

  /// <summary>A north-south node that refuses the one block named in <see cref="Refuses"/>.</summary>
  private sealed class JointNode : BlockNetworkNode {
    public Block? Refuses { get; init; }

    public override string NetworkType => "test";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.NORTH || face == BlockFacing.SOUTH;

    public override bool AcceptsNeighbour(Block neighbour) =>
      neighbour != Refuses;
  }
}
