using ExpandedLib.Assembling;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A cap recorded on a face of a node shuts that face on the lumped pipe network and keeps it from
/// joining a neighbour, and leaves the shape the orientation pass reads as it was.
/// </summary>
public class CapFaceTests {
  #region Fixtures

  private static readonly BlockPos RunPos = new(0, 0, 0);
  private static readonly string[] Axis = ["ns", "we", "ud"];

  private static TestWorld NewLumpedWorld(
    int openEnds,
    params BlockFacing[] capped
  ) {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var be = new BlockEntityAssembled();
    be.Behaviors.Add(new BEBehaviorAssembled(be));
    Cap(w, be, capped);
    w.Place(RunPos, OpenNode.With(openEnds), be);
    w.AddNode(RunPos, "pipe");
    return w;
  }

  private static void Cap(TestWorld w, BlockEntity be, BlockFacing[] faces) {
    Item cap = w.RegisterItem("test:cap");
    foreach (BlockFacing face in faces)
      be.GetBehavior<BEBehaviorAssembled>()
        .SetCap(face, new ItemStack(cap), null);
  }

  private static float TickedVolume(TestWorld w) {
    var net = Assert.IsType<PipeNetwork>(w.NetworkAt(RunPos));
    net.RestoreState(
      new PipeNetworkState {
        Volume = ExlibValues.LitresPerPipe,
        MediumType = "Steam",
      }
    );
    w.Tick();
    return net.State!.Volume;
  }

  private sealed class OpenNode : BlockNetworkNode {
    private static readonly BlockFacing[] Faces =
    [
      BlockFacing.UP,
      BlockFacing.NORTH,
    ];

    public int OpenEnds { get; init; }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      System.Array.IndexOf(Faces, face) is int i && i >= 0 && i < OpenEnds;

    public static OpenNode With(int openEnds) =>
      TestBlocks.Configure(
        new OpenNode { OpenEnds = openEnds },
        $"test:capnode-{openEnds}",
        openEnds
      );
  }

  private sealed class TeeNode : BlockNetworkNode {
    public override string NetworkType => "test";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.WEST
      || face == BlockFacing.EAST
      || face == BlockFacing.NORTH;

    public static TeeNode Create(int id) =>
      TestBlocks.Configure(new TeeNode(), "test:tee", id);
  }

  #endregion

  // Fails when HasConnectorAt ignores the cap record, so a capped end leaks.
  [Fact]
  public void Cap_LumpedSealsCappedEnd() {
    var w = NewLumpedWorld(1, BlockFacing.UP);

    Assert.Equal(ExlibValues.LitresPerPipe, TickedVolume(w), 3);
  }

  // Fails when a cap is read on a face with no record, so the open end beside a capped one shuts too.
  [Fact]
  public void Cap_LumpedOpenEndStillLeaks() {
    var w = NewLumpedWorld(2, BlockFacing.UP);

    Assert.Equal(
      ExlibValues.LitresPerPipe - ExlibValues.GasLeakRate,
      TickedVolume(w),
      3
    );
  }

  // Fails when the position-less connector test joins two pipes through a capped face.
  [Fact]
  public void Cap_CappedFaceDoesNotJoin() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    var a = new BlockPos(0, 0, 0);
    var b = new BlockPos(0, 0, 1);
    var be = new BlockEntityAssembled();
    be.Behaviors.Add(new BEBehaviorAssembled(be));
    Cap(w, be, [BlockFacing.SOUTH]);
    w.Place(a, TestNetworkBlock.Create("test", "ns", 1), be);
    w.AddNode(a, "test");
    w.Place(b, TestNetworkBlock.Create("test", "ns", 2));
    w.AddNode(b, "test");

    Assert.NotSame(w.NetworkAt(a), w.NetworkAt(b));
  }

  // Fails when the shape the orientation pass reads follows the cap, so a piece laid beside a capped tee turns away from it.
  [Fact]
  public void Cap_ShapeConnectorKeepsShape() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    TestNetworkBlock[] family = TestNetworkBlock.Family(
      "test",
      "test:node",
      "Axis",
      Axis
    );
    foreach (TestNetworkBlock block in family)
      w.Register(block);
    var capped = new BlockEntityAssembled();
    capped.Behaviors.Add(new BEBehaviorAssembled(capped));
    Cap(w, capped, [BlockFacing.WEST]);
    w.Place(new BlockPos(1, 0, 0), TeeNode.Create(10), capped);
    var entity = new OrientableNode {
      Orientation = "ns",
      PossibleOrientations = Axis,
    };
    w.Place(RunPos, family[0], entity);
    w.Initialize(entity);

    ((BlockNetworkNode)w.GetBlock(RunPos)).RecalculateAndSyncOrientations(
      w.World,
      RunPos
    );

    Assert.Equal("test:node-we", w.GetBlock(RunPos).Code.ToString());
  }
}
