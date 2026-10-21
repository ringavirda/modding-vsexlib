using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The joints an "mpshaft" run builds from the graph, the loss a frame takes, and the refusal of a placement that
/// would jam the run: <see cref="MpShaftNetwork"/>, <see cref="BlockBehaviorExNetworkPlacement"/> and
/// <see cref="BlockNetworkModSystem.CanPlaceMember"/>.
/// </summary>
public class MpShaftJointTests {
  private sealed class Cell
    : BlockEntity,
      IMpEnergyStorage,
      IMpEnergyProducer,
      IMpEnergyConsumer {
    public float Drive { get; set; }

    public float Load { get; set; }

    public float Inertia => 1f;

    public float DriveTorque(float speed) => Drive;

    public float LoadTorque(float speed) => Load;
  }

  private sealed class FrameEntity : BlockEntity, IMpEnergyLink {
    public NodeKey From => NodeKey.Port(Pos, 0);

    public NodeKey To => NodeKey.Port(Pos, 1);

    public float Ratio { get; set; } = 1f;

    public int Sign { get; set; } = 1;

    public float CapNm => 1000f;

    public float Efficiency { get; set; } = 1f;

    public bool Engaged => true;
  }

  private sealed class CellMember : BEBehaviorNetworkMember {
    public CellMember(BlockEntity be, string connectors)
      : base(be) {
      NetworkType = "mpshaft";
      DeclareConnectors(connectors);
    }
  }

  private sealed class FrameMember : BEBehaviorNetworkMember, INetworkMember {
    public FrameMember(BlockEntity be, string connectors)
      : base(be) {
      NetworkType = "mpshaft";
      DeclareConnectors(connectors);
    }

    IReadOnlyList<byte> INetworkMember.Slots(
      IBlockAccessor world,
      BlockPos pos
    ) => [NodeKey.PortBase, NodeKey.PortBase + 1];

    IEnumerable<NodeCrossing> INetworkMember.Crossings(
      IBlockAccessor world,
      NodeKey key
    ) {
      yield return new NodeCrossing(
        key.Slot == NodeKey.PortBase ? BlockFacing.WEST : BlockFacing.SOUTH
      );
    }
  }

  private sealed class RingBlock : Block, INetworkConnector {
    public string NetworkType => "mpshaft";

    public bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.EAST || face == BlockFacing.NORTH;
  }

  private sealed class CountingNetwork(BlockNetworkModSystem system)
    : BlockNetwork(system),
      INetworkPlacementCheck {
    public override string NetworkType => "counting";

    public int Asked { get; private set; }

    public string Code { get; set; } = "";

    public bool AcceptsMember(
      IBlockAccessor world,
      BlockPos pos,
      Block block,
      out string failureCode
    ) {
      Asked++;
      failureCode = Code;
      return Code == "";
    }

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) { }

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) { }
  }

  private sealed class PlainNetwork(BlockNetworkModSystem system)
    : BlockNetwork(system) {
    public override string NetworkType => "plain";

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) { }

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) { }
  }

  private static readonly BlockPos A = new(0, 0, 0);
  private static readonly BlockPos F = new(1, 0, 0);
  private static readonly BlockPos D = new(1, 0, 1);
  private static readonly BlockPos C = new(0, 0, 1);

  private static int _ids = 500;

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    return w;
  }

  private static void Put(
    TestWorld w,
    BlockPos pos,
    BlockEntity be,
    BEBehaviorNetworkMember member
  ) {
    be.Behaviors.Add(member);
    int id = _ids++;
    w.Place(pos, TestBlocks.Configure(new Block(), $"test:joint{id}", id), be);
    w.Initialize(be);
  }

  /// <summary>Cells A and D joined through a frame at F: A east to the frame's west port, the frame's south port to D.</summary>
  private static (
    TestWorld World,
    Cell Drive,
    Cell Load,
    FrameEntity Frame
  ) Line(
    float ratio = 1f,
    int sign = 1,
    float efficiency = 1f,
    float drive = 0f,
    float load = 0f
  ) {
    var w = NewWorld();
    var a = new Cell { Drive = drive };
    var d = new Cell { Load = load };
    var frame = new FrameEntity {
      Ratio = ratio,
      Sign = sign,
      Efficiency = efficiency,
    };
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, frame, new FrameMember(frame, "ws"));
    Put(w, D, d, new CellMember(d, "wn"));
    return (w, a, d, frame);
  }

  private static MpShaftNetwork Run(TestWorld w) =>
    (MpShaftNetwork)w.NetworkAt(A)!;

  private static void Tick(TestWorld w) =>
    Run(w).OnTick(w.Accessor, 0.2f, w.Networks);

  private static Block Closer(string json = "{}") {
    var block = TestBlocks.Configure(new RingBlock(), "test:closer", 499);
    var behaviour = new BlockBehaviorExNetworkPlacement(block);
    behaviour.Initialize(new JsonObject(JToken.Parse(json)));
    block.BlockBehaviors = [behaviour];
    block.CollectibleBehaviors = [behaviour];
    return block;
  }

  private static bool CanPlaceAt(
    TestWorld w,
    Block block,
    out string failureCode
  ) {
    failureCode = "";
    return block.CanPlaceBlock(
      w.World,
      null!,
      new BlockSelection { Position = C.Copy() },
      ref failureCode
    );
  }

  #region Joints

  // Fails when the run builds no joints from the graph on a topology change.
  [Fact]
  public void Joints_FaceCrossingsAndTheLinkJoinTheRun() {
    var (w, _, _, _) = Line();

    Tick(w);

    Assert.Equal(3, Run(w).Store.EdgeCount);
  }

  // Fails when a link's ratio or sign is left out of its joint.
  [Fact]
  public void Joints_LinkRatioAndSignSetTheRatioOfTheFarSide() {
    var (w, _, _, _) = Line(ratio: 2f, sign: -1);

    Tick(w);

    var store = Run(w).Store;
    float near = store.RatioOf(store.IndexOf(NodeKey.Centre(A)));
    float far = store.RatioOf(store.IndexOf(NodeKey.Centre(D)));
    Assert.Equal(-0.5f, far / near, 3);
  }

  // Fails when no joint exists to carry torque, so the torque reading stays 0 as before the joints.
  [Fact]
  public void Joints_TorqueReachesTheReadingOfBothCells() {
    var (w, _, _, _) = Line(drive: 50f, load: 50f);

    Tick(w);

    var store = Run(w).Store;
    Assert.Equal(50f, store.NodeTorque(store.IndexOf(NodeKey.Centre(A))), 1);
    Assert.Equal(50f, store.NodeTorque(store.IndexOf(NodeKey.Centre(D))), 1);
  }

  // Fails when the joined run is left outside group 0, which the run's speed and state are read from.
  [Fact]
  public void Joints_TheRunTurnsInGroupZero() {
    var (w, _, _, _) = Line(drive: 100f, load: 10f);

    Tick(w);
    Tick(w);

    Assert.True(Run(w).Speed > 0f);
    Assert.Equal(Run(w).Speed, Run(w).Store.Speed(0));
  }

  // Fails when the frame's efficiency is not applied as a mesh loss on the link's output.
  [Fact]
  public void Frame_TakesItsLossFromWhatItCarries() {
    float Gain(float efficiency) {
      var (w, _, _, _) = Line(efficiency: efficiency, drive: 100f, load: 90f);
      Tick(w);
      Tick(w);
      var store = Run(w).Store;
      int node = store.IndexOf(NodeKey.Centre(A));
      float before = store.NodeSpeed(node);
      Tick(w);
      return store.NodeSpeed(node) - before;
    }

    float lossless = Gain(1f);
    float frame = Gain(0.98f);

    Assert.True(frame < lossless);
    Assert.Equal(lossless - frame, 0.2f * 0.02f * 100f / 2f, 0.05f);
  }

  #endregion

  #region Placement

  // Fails when a placement that closes a ring of ratio 2 is accepted.
  [Fact]
  public void Place_JammingRingRefused() {
    var (w, _, _, _) = Line(ratio: 2f);
    Tick(w);

    Assert.False(CanPlaceAt(w, Closer(), out string code));
    Assert.Equal("exlib-mpshaft-jam", code);
  }

  // Fails when the sign of the link is left out of the ring's product.
  [Fact]
  public void Place_CrossedRingRefused() {
    var (w, _, _, _) = Line(sign: -1);
    Tick(w);

    Assert.False(CanPlaceAt(w, Closer(), out string code));
    Assert.Equal("exlib-mpshaft-jam", code);
  }

  // Fails when a ring whose ratio and sign product is 1 is refused.
  [Fact]
  public void Place_MatchedRingAccepted() {
    var (w, _, _, _) = Line();
    Tick(w);

    Assert.True(CanPlaceAt(w, Closer(), out string code));
    Assert.Equal("", code);
  }

  // Fails when the check joins the new cell into the live store instead of a copy.
  [Fact]
  public void Place_LeavesTheLiveStoreAsItWas() {
    var (w, _, _, _) = Line(ratio: 2f);
    Tick(w);
    var store = Run(w).Store;
    var before = (
      store.NodeCount,
      store.EdgeCount,
      store.GroupCount,
      store.Speed(0)
    );

    CanPlaceAt(w, Closer(), out _);
    var refused = (
      store.NodeCount,
      store.EdgeCount,
      store.GroupCount,
      store.Speed(0)
    );

    Assert.Equal(before, refused);
    var (w2, _, _, _) = Line();
    Tick(w2);
    var store2 = Run(w2).Store;
    var before2 = (
      store2.NodeCount,
      store2.EdgeCount,
      store2.GroupCount,
      store2.Speed(0)
    );
    CanPlaceAt(w2, Closer(), out _);
    Assert.Equal(
      before2,
      (store2.NodeCount, store2.EdgeCount, store2.GroupCount, store2.Speed(0))
    );
  }

  // Fails when the check reads only the nodes the live store already holds, which a run not yet ticked has none of.
  [Fact]
  public void Place_RefusesBeforeTheRunHasTicked() {
    var (w, _, _, _) = Line(ratio: 2f);

    Assert.False(CanPlaceAt(w, Closer(), out string code));
    Assert.Equal("exlib-mpshaft-jam", code);
  }

  // Fails when the behaviour ignores its declared network type, or defaults to another.
  [Fact]
  public void Placement_AsksTheDeclaredNetworkTypeAndDefaultsToMpshaft() {
    var (w, _, _, _) = Line(ratio: 2f);
    Tick(w);

    Assert.Equal(
      "mpshaft",
      new BlockBehaviorExNetworkPlacement(new Block()).NetworkType
    );
    Assert.True(
      CanPlaceAt(w, Closer("""{"networkType":"other"}"""), out string code)
    );
    Assert.Equal("", code);
  }

  // Fails when a network bordering the cell on two faces is asked twice.
  [Fact]
  public void CanPlaceMember_AsksEachBorderingNetworkOnce() {
    var w = NewWorld();
    w.RegisterNetwork("counting", sys => new CountingNetwork(sys));
    var a = new Cell();
    var f = new Cell();
    var d = new Cell();
    Put(w, A, a, new CellMemberOf(a, "es", "counting"));
    Put(w, F, f, new CellMemberOf(f, "ws", "counting"));
    Put(w, D, d, new CellMemberOf(d, "wn", "counting"));
    var net = (CountingNetwork)w.NetworkAt(A)!;
    Assert.Same(net, w.NetworkAt(D));
    string code = "";

    Assert.True(
      w.Networks.CanPlaceMember(
        w.Accessor,
        C,
        new Block(),
        "counting",
        ref code
      )
    );

    Assert.Equal(1, net.Asked);
  }

  // Fails when a refusal's code is not handed back to the caller.
  [Fact]
  public void CanPlaceMember_HandsBackTheRefusingCode() {
    var w = NewWorld();
    w.RegisterNetwork("counting", sys => new CountingNetwork(sys));
    var a = new Cell();
    Put(w, A, a, new CellMemberOf(a, "es", "counting"));
    ((CountingNetwork)w.NetworkAt(A)!).Code = "test-refused";
    string code = "";

    Assert.False(
      w.Networks.CanPlaceMember(
        w.Accessor,
        C,
        new Block(),
        "counting",
        ref code
      )
    );
    Assert.Equal("test-refused", code);
  }

  // Fails when a network type without the check is refused or asked.
  [Fact]
  public void CanPlaceMember_TakesAnyMemberForATypeWithoutTheCheck() {
    var w = NewWorld();
    w.RegisterNetwork("plain", sys => new PlainNetwork(sys));
    var a = new Cell();
    Put(w, A, a, new CellMemberOf(a, "es", "plain"));
    string code = "";

    Assert.True(
      w.Networks.CanPlaceMember(w.Accessor, C, new Block(), "plain", ref code)
    );
    Assert.Equal("", code);
  }

  // Fails when a block that is no network member is refused by the mpshaft check.
  [Fact]
  public void Place_APlainBlockIsAccepted() {
    var (w, _, _, _) = Line(ratio: 2f);
    Tick(w);
    string code = "";

    Assert.True(
      w.Networks.CanPlaceMember(w.Accessor, C, new Block(), "mpshaft", ref code)
    );
  }

  // Fails when BlockNetworkNode places without asking the placement behaviour.
  [Fact]
  public void NetworkNodePlacement_ObeysTheBehavioursRefusal() {
    var w = NewWorld();
    w.RegisterNetwork("counting", sys => new CountingNetwork(sys));
    w.PlaceNode(A, "counting", "we");
    ((CountingNetwork)w.NetworkAt(A)!).Code = "test-refused";
    var family = TestNetworkBlock.Family(
      "counting",
      "test:placed",
      "Axis",
      ["ns", "we", "ud"],
      700
    );
    foreach (var variant in family) {
      var behaviour = new BlockBehaviorExNetworkPlacement(variant);
      behaviour.Initialize(
        new JsonObject(JToken.Parse("""{"networkType":"counting"}"""))
      );
      variant.BlockBehaviors = [.. variant.BlockBehaviors, behaviour];
      variant.CollectibleBehaviors =
      [
        .. variant.CollectibleBehaviors,
        behaviour,
      ];
      w.Register(variant);
    }
    var block = family[1];
    string code = "";

    bool placed = block.TryPlaceBlock(
      w.World,
      null!,
      new ItemStack(block),
      new BlockSelection { Position = F.Copy(), Face = BlockFacing.EAST },
      ref code
    );

    Assert.False(placed);
    Assert.Equal("test-refused", code);
  }

  #endregion

  private sealed class CellMemberOf : BEBehaviorNetworkMember {
    public CellMemberOf(BlockEntity be, string connectors, string networkType)
      : base(be) {
      NetworkType = networkType;
      DeclareConnectors(connectors);
    }
  }
}
