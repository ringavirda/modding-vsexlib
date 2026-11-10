using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A belt strung between two cells that share no face: <see cref="BEBehaviorMpBelt"/> through
/// <see cref="INetworkFarNeighbours"/>, <see cref="BlockNetworkModSystem.Relink"/> and the placement check of
/// <see cref="MpShaftNetwork"/>.
/// </summary>
public class MpBeltAcrossTests {
  private sealed class Cell(float inertia)
    : BlockEntity,
      IMpEnergyStorage,
      IMpEnergyProducer {
    public Cell()
      : this(100f) { }

    public float Drive { get; set; }

    public float Inertia => inertia;

    public float DriveTorque(float speed) => Drive;
  }

  private sealed class Host : BlockEntity;

  private sealed class FrameEntity : BlockEntity, IMpEnergyLink {
    public NodeKey From => NodeKey.Port(Pos, 0);

    public NodeKey To => NodeKey.Port(Pos, 1);

    public float Ratio => 1f;

    public int Sign => 1;

    public float CapNm => 1e9f;

    public float Efficiency => 1f;

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
    public FrameMember(BlockEntity be)
      : base(be) {
      NetworkType = "mpshaft";
      DeclareConnectors("ws");
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

  private sealed class PulleyBlock(IMpEnergyLink link)
    : Block,
      INetworkConnector,
      IMpEnergyLinkSource {
    public string NetworkType => "mpshaft";

    public bool HasConnectorAt(BlockFacing face) => face == BlockFacing.NORTH;

    public IEnumerable<IMpEnergyLink> LinksAt(
      IBlockAccessor world,
      BlockPos pos
    ) => [link];
  }

  private static readonly BlockPos A = new(0, 0, 0);
  private static readonly BlockPos B = new(0, 0, 4);
  private static readonly BlockPos F = new(1, 0, 0);
  private static readonly BlockPos D = new(1, 0, 1);
  private static readonly BlockPos C = new(0, 0, 1);

  private static int _ids = 3000;

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    return w;
  }

  private static void Put(
    TestWorld w,
    BlockPos pos,
    BlockEntity be,
    params BlockEntityBehavior[] behaviours
  ) => Put(w, pos, be, null, behaviours);

  private static void Put(
    TestWorld w,
    BlockPos pos,
    BlockEntity be,
    string? entityClass,
    params BlockEntityBehavior[] behaviours
  ) {
    foreach (var behaviour in behaviours)
      be.Behaviors.Add(behaviour);
    int id = _ids++;
    Block block = TestBlocks.Configure(new Block(), $"test:across{id}", id);
    block.EntityClass = entityClass;
    w.Place(pos, block, be);
    w.Initialize(be);
  }

  private sealed record Pair(
    TestWorld World,
    Cell Near,
    Cell Far,
    BEBehaviorMpBelt Held,
    BEBehaviorMpBelt Back
  );

  /// <summary>Cells at <paramref name="near"/> and <paramref name="far"/>, each on a run of its own with a belt behaviour, nothing strung.</summary>
  private static Pair Cells(BlockPos near, BlockPos far, bool farBelt = true) {
    var w = NewWorld();
    var a = new Cell(100f);
    var b = new Cell(100f);
    var heldBelt = new BEBehaviorMpBelt(a);
    var backBelt = new BEBehaviorMpBelt(b);
    Put(w, near, a, new CellMember(a, ""), heldBelt);
    Put(
      w,
      far,
      b,
      farBelt ? [new CellMember(b, ""), backBelt] : [new CellMember(b, "")]
    );
    return new Pair(w, a, b, heldBelt, backBelt);
  }

  private static void String(Pair p, BlockPos near, BlockPos far) =>
    p.Held.Connect(NodeKey.Centre(near), NodeKey.Centre(far), 1, 1);

  private static MpShaftNetwork Run(TestWorld w, BlockPos pos) =>
    (MpShaftNetwork)w.NetworkAt(pos)!;

  private static void Tick(TestWorld w, BlockPos pos) =>
    Run(w, pos).OnTick(w.Accessor, 0.2f, w.Networks);

  private static float SpeedAt(TestWorld w, BlockPos pos) {
    var store = Run(w, pos).Store;
    return store.NodeSpeed(store.IndexOf(NodeKey.Centre(pos)));
  }

  // Fails when the walk joins no far cell, so the far run stands.
  [Fact]
  public void Belt_across_cells_drives_the_far_run() {
    var p = Cells(A, B);
    p.Near.Drive = 1000f;
    String(p, A, B);

    for (int i = 0; i < 20; i++)
      Tick(p.World, A);

    Assert.Same(p.World.NetworkAt(A), p.World.NetworkAt(B));
    Assert.True(SpeedAt(p.World, A) > 10f);
    Assert.True(SpeedAt(p.World, B) > 10f);
    Assert.Equal(B, p.Held.FarCell);
    Assert.Equal(A, p.Back.FarCell);
    Assert.True(p.Held.Engaged);
    Assert.False(p.Back.Engaged);
  }

  // Fails when a cell is joined to a far cell that does not list it back.
  [Fact]
  public void One_sided_far_neighbour_joins_nothing() {
    var p = Cells(A, B, farBelt: false);
    p.Near.Drive = 1000f;
    String(p, A, B);

    for (int i = 0; i < 20; i++) {
      Tick(p.World, A);
      Tick(p.World, B);
    }

    Assert.NotSame(p.World.NetworkAt(A), p.World.NetworkAt(B));
    Assert.True(SpeedAt(p.World, A) > 10f);
    Assert.Equal(0f, SpeedAt(p.World, B), 3);
  }

  // Fails when taking the belt off leaves the runs one network, so the far run turns on a dead link.
  [Fact]
  public void Disconnect_splits_the_network() {
    var p = Cells(A, B);
    p.Near.Drive = 1000f;
    String(p, A, B);
    for (int i = 0; i < 20; i++)
      Tick(p.World, A);
    Assert.Same(p.World.NetworkAt(A), p.World.NetworkAt(B));

    p.Back.Disconnect();
    Tick(p.World, B);
    float kept = SpeedAt(p.World, B);
    float before = SpeedAt(p.World, A);
    for (int i = 0; i < 20; i++) {
      Tick(p.World, A);
      Tick(p.World, B);
    }

    Assert.NotSame(p.World.NetworkAt(A), p.World.NetworkAt(B));
    Assert.Null(p.Held.FarCell);
    Assert.Null(p.Back.FarCell);
    Assert.False(p.Held.Engaged);
    Assert.True(SpeedAt(p.World, A) > before + 1f);
    Assert.Equal(kept, SpeedAt(p.World, B), 3);
  }

  /// <summary>Cells A, F (a 1:1 frame), D on one run, and the pulley block that would close the ring at C with a belt to D.</summary>
  private static (TestWorld World, Block Pulley) Ring(int toSize) {
    var w = NewWorld();
    var a = new Cell(100f);
    var d = new Cell(100f);
    var frame = new FrameEntity();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, frame, new FrameMember(frame));
    Put(w, D, d, new CellMember(d, "wn"));
    var host = new Host { Pos = C.Copy() };
    var belt = new BEBehaviorMpBelt(host);
    belt.Connect(NodeKey.Centre(C), NodeKey.Centre(D), 1, toSize);
    var pulley = TestBlocks.Configure(
      new PulleyBlock(belt),
      "test:pulley",
      2999
    );
    return (w, pulley);
  }

  private static bool Accepts(TestWorld w, Block pulley, out string code) =>
    Run(w, A).AcceptsMember(w.Accessor, C, pulley, out code);

  // Fails when the placement check skips the links that reach another cell.
  [Fact]
  public void Ring_at_1_to_2_jams() {
    var (w, pulley) = Ring(toSize: 2);

    Assert.False(Accepts(w, pulley, out string code));
    Assert.Equal("exlib-mpshaft-jam", code);
  }

  // Fails when every ring a far belt closes is refused.
  [Fact]
  public void Ring_at_1_to_1_runs() {
    var (w, pulley) = Ring(toSize: 1);

    Assert.True(Accepts(w, pulley, out string code));
    Assert.Equal("", code);
  }

  // Fails when the far cell is left out of the saved belt, so a reloaded pair joins nothing.
  [Fact]
  public void Saved_belt_rejoins_after_load() {
    var p = Cells(A, B);
    String(p, A, B);
    var heldTree = new TreeAttribute();
    var backTree = new TreeAttribute();
    p.Held.ToTreeAttributes(heldTree);
    p.Back.ToTreeAttributes(backTree);

    var w = NewWorld();
    var a = new Cell(100f) { Drive = 1000f };
    var b = new Cell(100f);
    var held = new BEBehaviorMpBelt(a);
    var back = new BEBehaviorMpBelt(b);
    held.FromTreeAttributes(heldTree, w.World);
    back.FromTreeAttributes(backTree, w.World);
    Put(w, A, a, new CellMember(a, ""), held);
    Put(w, B, b, new CellMember(b, ""), back);

    Assert.Same(w.NetworkAt(A), w.NetworkAt(B));
    for (int i = 0; i < 20; i++)
      Tick(w, A);
    Assert.True(SpeedAt(w, B) > 10f);
    Assert.Equal(B, held.FarCell);
    Assert.True(held.Engaged);
    Assert.False(back.Engaged);
  }

  // Fails when a far cell in an unloaded chunk is taken for gone: the run splits and the link is not kept.
  [Fact]
  public void Unloaded_far_cell_idles_the_link() {
    var far = new BlockPos(GlobalConstants.ChunkSize * 2, 0, 0);
    var p = Cells(A, far);
    p.Near.Drive = 1000f;
    String(p, A, far);
    Assert.Same(p.World.NetworkAt(A), p.World.NetworkAt(far));

    p.World.UnloadChunkAt(far, unloadEntities: true);
    p.World.Networks.Relink(p.World.Accessor, A);

    Assert.Same(p.World.NetworkAt(A), p.World.NetworkAt(far));
    Assert.Equal(far, p.Held.FarCell);
    Assert.True(p.Held.Engaged);

    p.World.LoadChunkAt(far);
    for (int i = 0; i < 20; i++)
      Tick(p.World, A);
    Assert.True(SpeedAt(p.World, far) > 10f);
  }

  // Fails when restringing over a back pointer the far end kept releases the new holder.
  [Fact]
  public void Restring_over_a_stale_back_pointer_joins() {
    var far = new BlockPos(GlobalConstants.ChunkSize * 2, 0, 0);
    var w = NewWorld();
    w.RegisterBlockEntityFactory(
      "acrossFar",
      () => {
        var cell = new Cell(100f);
        cell.Behaviors.Add(new CellMember(cell, ""));
        cell.Behaviors.Add(new BEBehaviorMpBelt(cell));
        return cell;
      }
    );
    var a = new Cell(100f);
    var held = new BEBehaviorMpBelt(a);
    Put(w, A, a, new CellMember(a, ""), held);
    var b = new Cell(100f);
    Put(w, far, b, "acrossFar", new CellMember(b, ""), new BEBehaviorMpBelt(b));
    held.Connect(NodeKey.Centre(A), NodeKey.Centre(far), 1, 1);
    w.UnloadChunkAt(far, unloadEntities: true);
    held.Disconnect();
    w.LoadChunkAt(far);

    held.Connect(NodeKey.Centre(A), NodeKey.Centre(far), 1, 1);

    Assert.True(held.Engaged);
    Assert.Same(w.NetworkAt(A), w.NetworkAt(far));
  }

  // Fails when the back end reads its own unfitted belt instead of the holder's.
  [Fact]
  public void Back_end_reads_the_holders_belt() {
    var p = Cells(A, B);
    p.Held.Connect(NodeKey.Centre(A), NodeKey.Centre(B), 1, 2);
    p.Held.Wear = 0.5f;

    Assert.Equal(2f, p.Back.Ratio);
    Assert.Equal(0.5f, p.Back.Wear);
    Assert.Equal(p.Held.CapNm, p.Back.CapNm);
  }

  // Fails when a belt state change is not marked dirty on both ends.
  [Fact]
  public void Connect_and_disconnect_mark_both_ends_dirty() {
    var p = Cells(A, B);
    var acc = p.World.Accessor;
    acc.ClearReceivedCalls();

    String(p, A, B);
    acc.Received().MarkBlockEntityDirty(A);
    acc.Received().MarkBlockEntityDirty(B);
    acc.ClearReceivedCalls();
    p.Held.Disconnect();
    acc.Received().MarkBlockEntityDirty(A);
    acc.Received().MarkBlockEntityDirty(B);
  }

  // Fails when a tree without the far keys leaves the old far cell on the behaviour.
  [Fact]
  public void Tree_without_far_keys_clears_the_far_cell() {
    var p = Cells(A, B);
    String(p, A, B);
    var tree = new TreeAttribute();
    p.Back.Disconnect();
    p.Held.ToTreeAttributes(tree);
    String(p, A, B);

    p.Held.FromTreeAttributes(tree, p.World.World);

    Assert.Null(p.Held.FarCell);
  }
}
