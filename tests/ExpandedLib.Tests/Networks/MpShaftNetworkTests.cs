using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// How the "mpshaft" run merges, splits, refuses a jamming ring, holds still behind an unloaded chunk and
/// restores: <see cref="MpShaftNetwork"/> and the group operations of <see cref="MpShaftStore"/>.
/// </summary>
public class MpShaftNetworkTests {
  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    return w;
  }

  private static BlockPos Cell(int x) => new(x, 0, 0);

  private static NodeKey Key(int x) => NodeKey.Centre(Cell(x));

  private static MpShaftNetwork RunAt(TestWorld w, int x) =>
    (MpShaftNetwork)w.NetworkAt(Cell(x))!;

  private static void Lay(TestWorld w, params int[] xs) {
    foreach (int x in xs)
      w.PlaceNode(Cell(x), "mpshaft", "we");
  }

  /// <summary>Puts every node of <paramref name="run"/> in one new group at <paramref name="speed"/>, each holding <paramref name="inertia"/> kg*m^2.</summary>
  private static void Seed(MpShaftNetwork run, float inertia, float speed) {
    int group = run.Store.AddGroup(speed);
    foreach (var key in run.NodeKeys.OrderBy(k => k.Pos.X))
      run.Store.AddNode(key, group, inertia, 0f);
  }

  private sealed class FlywheelNode : BlockEntity, IMpEnergyStorage {
    public float Inertia { get; init; }
  }

  private sealed class DriveNode : BlockEntity, IMpEnergyProducer {
    public float Torque { get; set; }

    public float DriveTorque(float speed) => Torque;
  }

  /// <summary>Builds one network holding the given nodes, bypassing the graph.</summary>
  private static MpShaftNetwork Wire(
    TestWorld world,
    params (BlockPos Pos, BlockEntity Be)[] nodes
  ) {
    var net = new MpShaftNetwork(world.Networks);
    int id = 100;
    foreach (var (pos, be) in nodes) {
      world.Place(
        pos,
        TestBlocks.Configure(new Block(), $"test:mpshaft{id}", id),
        be
      );
      net.Nodes.Add(pos);
      net.NodeKeys.Add(NodeKey.Centre(pos));
      id++;
    }
    return net;
  }

  #region Merge

  // Fails when the joined speed is the energy pool or a plain mean instead of the momentum mean.
  [Fact]
  public void Merge_ConservesMomentum() {
    var w = NewWorld();
    Lay(w, 0, 1, 3, 4);
    Seed(RunAt(w, 0), 5f, 2f);
    Seed(RunAt(w, 3), 15f, 6f);

    Lay(w, 2);

    var run = RunAt(w, 0);
    Assert.Same(run, RunAt(w, 4));
    Assert.Equal(4, run.Store.NodeCount);
    Assert.All(
      new[] { 0, 1, 3, 4 },
      x => Assert.Equal(0, run.Store.GroupOf(run.Store.IndexOf(Key(x))))
    );
    Assert.Equal((10f * 2f + 30f * 6f) / 40f, run.Store.Speed(0), 4);
  }

  // Fails when a node's speed enters the mean as its group's speed, losing the sign of its ratio.
  [Fact]
  public void Merge_SignedRatioCountsAgainstTheMean() {
    var into = new MpShaftStore();
    into.AddNode(Key(0), into.AddGroup(2f), 10f, 0f);
    var other = new MpShaftStore();
    other.AddNode(Key(1), other.AddGroup(2f), 10f, 0f, -1f);

    into.Absorb(other);

    Assert.Equal(-1f, into.RatioOf(into.IndexOf(Key(1))));
    Assert.Equal(2f, into.Speed(0), 4);
  }

  // Fails when the second group enters with ratio 1 instead of the joint's.
  [Fact]
  public void Merge_AcrossJointEntersWithOwnRatio() {
    var store = new MpShaftStore();
    int a = store.AddNode(Key(0), store.AddGroup(4f), 10f, 0f);
    int b = store.AddNode(Key(1), store.AddGroup(8f), 2.5f, 0f);

    Assert.True(store.TryAddEdge(a, b, 2f, 1));

    Assert.Equal(0.5f, store.RatioOf(b), 4);
    Assert.Equal(store.GroupOf(a), store.GroupOf(b));
    float momentum = 10f * 4f + 2.5f * 0.5f * 8f;
    float reflected = 10f + 2.5f * 0.25f;
    Assert.Equal(momentum / reflected, store.Speed(store.GroupOf(a)), 4);
  }

  #endregion

  #region Split

  // Fails when a fragment's group starts at rest.
  [Fact]
  public void Split_FragmentsKeepTheRunSpeed() {
    var w = NewWorld();
    Lay(w, 0, 1, 2, 3, 4);
    var run = RunAt(w, 0);
    Seed(run, 3f, 4f);
    run.Store.TryAddEdge(
      run.Store.IndexOf(Key(0)),
      run.Store.IndexOf(Key(1)),
      1f,
      1
    );
    run.Store.TryAddEdge(
      run.Store.IndexOf(Key(3)),
      run.Store.IndexOf(Key(4)),
      1f,
      1
    );

    w.RemoveNode(Cell(2));

    var near = RunAt(w, 0);
    var far = RunAt(w, 3);
    Assert.NotSame(near, far);
    Assert.Equal(2, near.Store.NodeCount);
    Assert.Equal(2, far.Store.NodeCount);
    Assert.Equal(4f, near.Store.Speed(0), 4);
    Assert.Equal(4f, far.Store.Speed(0), 4);
    Assert.Equal(1, near.Store.EdgeCount);
    Assert.Equal(1, far.Store.EdgeCount);
  }

  #endregion

  #region Loops

  private static MpShaftStore Ring(out int a, out int b, out int c) {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    a = store.AddNode(Key(0), group, 1f, 0f);
    b = store.AddNode(Key(1), group, 1f, 0f, 0.5f);
    c = store.AddNode(Key(2), group, 1f, 0f, 0.25f);
    return store;
  }

  // Fails when the closing joint is accepted whatever its ratio.
  [Fact]
  public void Loop_MismatchedRingRefused() {
    var store = Ring(out int a, out int b, out int c);
    Assert.True(store.TryAddEdge(a, b, 2f, 1));
    Assert.True(store.TryAddEdge(b, c, 2f, 1));

    Assert.False(store.TryAddEdge(c, a, 1f, 1));
    Assert.Equal(2, store.EdgeCount);
    Assert.True(store.TryAddEdge(c, a, 0.25f, 1));
    Assert.Equal(3, store.EdgeCount);
  }

  // Fails when the sign of a joint is left out of the ring's product.
  [Fact]
  public void Loop_CrossedOddRingRefused() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int a = store.AddNode(Key(0), group, 1f, 0f);
    int b = store.AddNode(Key(1), group, 1f, 0f, -1f);
    int c = store.AddNode(Key(2), group, 1f, 0f, 1f);
    Assert.True(store.TryAddEdge(a, b, 1f, -1));
    Assert.True(store.TryAddEdge(b, c, 1f, -1));

    Assert.False(store.TryAddEdge(c, a, 1f, -1));
    Assert.True(store.TryAddEdge(c, a, 1f, 1));
  }

  #endregion

  #region Suspension

  // Fails when the topology review drops the nodes behind the unloaded chunk or their speed.
  [Fact]
  public void Suspend_KeepsTheRunWhole() {
    var w = NewWorld();
    int boundary = GlobalConstants.ChunkSize;
    Lay(w, boundary - 2, boundary - 1, boundary, boundary + 1);
    var run = RunAt(w, boundary);
    Seed(run, 2f, 3f);
    w.UnloadChunkAt(Cell(boundary));

    w.RemoveNode(Cell(boundary - 2));

    Assert.Single(w.Networks.AllNetworks);
    Assert.Equal(3, run.Store.NodeCount);
    Assert.Equal(3f, run.Store.Speed(0), 4);
  }

  // Fails when a run steps with a node in an unloaded chunk.
  [Fact]
  public void Suspend_HoldsSpeedUntilEveryNodeLoads() {
    var w = new TestWorld();
    int boundary = GlobalConstants.ChunkSize;
    var net = Wire(
      w,
      (Cell(boundary - 2), new FlywheelNode { Inertia = 10f }),
      (Cell(boundary - 1), new DriveNode { Torque = 5f }),
      (Cell(boundary), new FlywheelNode())
    );
    net.OnTick(w.Accessor, 0.2f, w.Networks);
    float running = net.Speed;
    Assert.True(running > 0f);

    w.UnloadChunkAt(Cell(boundary));
    net.OnTick(w.Accessor, 0.2f, w.Networks);
    net.OnTick(w.Accessor, 0.2f, w.Networks);
    Assert.Equal(running, net.Speed);

    w.LoadChunkAt(Cell(boundary));
    net.OnTick(w.Accessor, 0.2f, w.Networks);
    Assert.True(net.Speed > running);
  }

  #endregion

  #region Restore

  // Fails when a restore replaces the speed already waiting instead of mixing by momentum.
  [Fact]
  public void Restore_FlywheelsMeetAtTheMomentumMean() {
    var w = new TestWorld();
    var net = Wire(
      w,
      (Cell(0), new FlywheelNode { Inertia = 10f }),
      (Cell(1), new FlywheelNode { Inertia = 30f })
    );

    net.RestoreState(new MpShaftRunState(2f, 10f));
    net.RestoreState(new MpShaftRunState(6f, 30f));
    net.OnTick(w.Accessor, 0.2f, w.Networks);

    Assert.Equal(5f, net.Speed, 4);
    Assert.Equal(5f, net.Store.Speed(0), 4);
  }

  // Fails when the restored speed joins after the new flywheel's inertia is already in the run.
  [Fact]
  public void Restore_LateFlywheelMixesWithTheLiveRun() {
    var w = new TestWorld();
    var net = Wire(w, (Cell(0), new FlywheelNode { Inertia = 10f }));
    net.RestoreState(new MpShaftRunState(4f, 10f));
    net.OnTick(w.Accessor, 0.2f, w.Networks);

    w.Place(
      Cell(1),
      TestBlocks.Configure(new Block(), "test:mpshaft-late", 150),
      new FlywheelNode { Inertia = 10f }
    );
    net.Nodes.Add(Cell(1));
    net.NodeKeys.Add(Key(1));
    net.RestoreState(new MpShaftRunState(0f, 10f));
    net.OnTick(w.Accessor, 0.2f, w.Networks);

    Assert.Equal(2f, net.Speed, 4);
  }

  // Fails when the run or the strain is not written under its key, or the version is not checked.
  [Fact]
  public void Restore_RunAndStrainRoundTripTheTree() {
    var w = new TestWorld();
    var saved = Wire(w, (Cell(0), new FlywheelNode { Inertia = 10f }));
    saved.RestoreState(new MpShaftRunState(3f, 10f));
    saved.OnTick(w.Accessor, 0.2f, w.Networks);
    var tree = new TreeAttribute();
    saved.SaveRun(tree, 10f);
    MpShaftSave.WriteStrain(tree, 0.3f);

    var loaded = new MpShaftNetwork(w.Networks);
    Assert.True(loaded.TryRestoreRun(tree));
    Assert.Equal(3f, loaded.Speed, 4);
    Assert.Equal(0.3f, MpShaftSave.ReadStrain(tree), 4);

    tree.GetTreeAttribute(MpShaftSave.RunKey).SetInt("v", 99);
    var stale = new MpShaftNetwork(w.Networks);
    Assert.False(stale.TryRestoreRun(tree));
    Assert.Equal(0f, stale.Speed);
  }

  #endregion
}
