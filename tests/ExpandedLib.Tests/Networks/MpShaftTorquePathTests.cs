using System.Collections.Generic;
using System.Diagnostics;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>
/// Where the "mpshaft" torque went after a step, and when a segment's reading is written:
/// <see cref="MpShaftStore.SolveTorquePath"/>, <see cref="MpReadingGate"/> and <see cref="MpSegmentReading"/>.
/// </summary>
public class MpShaftTorquePathTests(ITestOutputHelper output) {
  private static NodeKey Key(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private static MpShaftStore Chain(
    int count,
    float speed,
    out int[] nodes,
    float inertia = 0f
  ) {
    var store = new MpShaftStore();
    int group = store.AddGroup(speed);
    nodes = new int[count];
    for (int i = 0; i < count; i++)
      nodes[i] = store.AddNode(Key(i), group, inertia, 0f);
    for (int i = 1; i < count; i++)
      Assert.True(store.TryAddEdge(nodes[i - 1], nodes[i], 1f, 1));
    return store;
  }

  private sealed class SegmentNode : BlockEntity, IMpEnergySegment {
    public MpShaftSize Size => MpShaftSize.Line;

    public float RatingNm => 100f;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }

    public MpSegmentReading? Stored { get; private set; }

    public int Stores { get; private set; }

    public void StoreReading(MpSegmentReading reading) {
      Stored = reading;
      Stores++;
    }
  }

  #region Path

  // Fails when an edge carries only its own end's torque instead of the sum beyond it.
  [Fact]
  public void TorquePath_ChainCarriesSubtreeSum() {
    var store = Chain(3, 10f, out var n);
    store.SetDrive(n[0], 100f);
    store.SetLoad(n[1], 30f);
    store.SetLoad(n[2], 70f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(100f, store.EdgeTorque(0), 3);
    Assert.Equal(70f, store.EdgeTorque(1), 3);
    Assert.Equal(100f, store.NodeTorque(n[1]), 3);
  }

  // Fails when the inertial reaction -I alpha is left out of a node's net torque.
  [Fact]
  public void TorquePath_AcceleratingRunCarriesInertialReaction() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int a = store.AddNode(Key(0), group, 2f, 0f);
    int b = store.AddNode(Key(1), group, 3f, 0f);
    int c = store.AddNode(Key(2), group, 5f, 0f);
    store.TryAddEdge(a, b, 1f, 1);
    store.TryAddEdge(b, c, 1f, 1);
    store.SetDrive(a, 100f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    float alpha = store.Speed(group) / 0.2f;
    Assert.Equal(10f, alpha, 3);
    Assert.Equal(5f * alpha, store.EdgeTorque(1), 3);
    Assert.Equal(8f * alpha, store.EdgeTorque(0), 3);
    Assert.Equal(
      0f,
      store.NetTorque(a) + store.NetTorque(b) + store.NetTorque(c),
      3
    );
  }

  // Fails when a run held at rest by standing torque carries the whole standing torque instead of the static share.
  [Fact]
  public void TorquePath_RestingRunCarriesStaticShare() {
    var store = Chain(2, 0f, out var n, 1f);
    store.SetDrive(n[0], 10f);
    store.SetLoad(n[1], 0f);
    store.SetStanding(n[1], 40f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(0f, store.Speed(0));
    Assert.Equal(10f, store.EdgeTorque(0), 3);
    Assert.Equal(10f, store.NodeTorque(n[1]), 3);
  }

  // Fails when the standing torque of a step that reverses the run is taken in the direction it ended in.
  [Fact]
  public void TorquePath_ReversingStepSumsToZero() {
    var store = new MpShaftStore();
    int group = store.AddGroup(1f);
    int a = store.AddNode(Key(0), group, 1f, 10f);
    int b = store.AddNode(Key(1), group, 0f, 0f, -1f);
    store.SetDrive(b, 12f);

    store.Step(1f, 100f);
    store.SolveTorquePath();

    Assert.Equal(-21f, store.Speed(group), 3);
    Assert.Equal(
      0f,
      store.RatioOf(a) * store.NetTorque(a)
        + store.RatioOf(b) * store.NetTorque(b),
      3
    );
  }

  // Fails when the root of every component is found by scanning every node.
  [Fact]
  public void TorquePath_JointlessRunSolvesInLinearTime() {
    var store = new MpShaftStore();
    int group = store.AddGroup(1f);
    for (int i = 0; i < 5000; i++)
      store.SetDrive(store.AddNode(Key(i), group, 1f, 0f), 1f);
    var clock = Stopwatch.StartNew();

    for (int i = 0; i < 50; i++) {
      store.Step(0.2f, 100f);
      store.SolveTorquePath();
    }

    Assert.True(
      clock.ElapsedMilliseconds < 400,
      $"{clock.ElapsedMilliseconds} ms"
    );
  }

  // Fails when a ring is walked as a tree, sending all torque down one path instead of splitting by stiffness.
  [Fact]
  public void TorquePath_RingSplitsByStiffness() {
    var store = new MpShaftStore();
    int group = store.AddGroup(10f);
    var n = new int[4];
    for (int i = 0; i < 4; i++)
      n[i] = store.AddNode(Key(i), group, 0f, 0f);
    store.TryAddEdge(n[0], n[1], 1f, 1, 3f);
    store.TryAddEdge(n[1], n[2], 1f, 1, 3f);
    store.TryAddEdge(n[0], n[3], 1f, 1, 1f);
    store.TryAddEdge(n[3], n[2], 1f, 1, 1f);
    store.SetDrive(n[0], 100f);
    store.SetLoad(n[2], 100f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(75f, store.EdgeTorque(0), 2);
    Assert.Equal(75f, store.EdgeTorque(1), 2);
    Assert.Equal(25f, store.EdgeTorque(2), 2);
    Assert.Equal(25f, store.EdgeTorque(3), 2);
  }

  // Fails when a geared joint carries the same torque on both sides instead of scaling it by the ratio.
  [Fact]
  public void TorquePath_GearedJointScalesTorque() {
    var store = new MpShaftStore();
    int a = store.AddNode(Key(0), store.AddGroup(10f), 0f, 0f);
    int b = store.AddNode(Key(1), store.AddGroup(10f), 0f, 0f);
    Assert.True(store.TryAddEdge(a, b, 2f, 1));
    store.SetDrive(a, 100f);
    store.SetLoad(b, 200f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(100f, store.NodeTorque(a), 3);
    Assert.Equal(200f, store.NodeTorque(b), 3);
  }

  // Fails when the elimination order is rebuilt every step instead of only on a topology change.
  [Fact]
  public void TorquePath_OrderCachedUntilJointsChange() {
    var store = new MpShaftStore();
    int group = store.AddGroup(10f);
    var n = new int[4];
    for (int i = 0; i < 4; i++)
      n[i] = store.AddNode(Key(i), group, 0f, 0f);
    store.TryAddEdge(n[0], n[1], 1f, 1);
    store.TryAddEdge(n[1], n[2], 1f, 1);
    store.TryAddEdge(n[2], n[0], 1f, 1);
    store.SetDrive(n[0], 50f);
    store.SetLoad(n[1], 50f);

    for (int i = 0; i < 3; i++) {
      store.Step(0.2f, 100f);
      store.SolveTorquePath();
    }
    Assert.Equal(1, store.PathOrderBuilds);

    store.TryAddEdge(n[2], n[3], 1f, 1);
    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(2, store.PathOrderBuilds);
  }

  #endregion

  #region Readings

  // Fails when the gate admits a change under 2 % of rating, or a second write inside one second.
  [Fact]
  public void Reading_GateWaitsForTwoPercentAndASecond() {
    var gate = new MpReadingGate();
    var at = (float torque, float strain) =>
      new MpSegmentReading(torque, 100f, strain, 1f, MpShaftSize.Line);

    Assert.True(gate.TryAdmit(Key(0), at(10f, 0f), 0f));
    Assert.False(gate.TryAdmit(Key(0), at(30f, 0f), 0.5f));
    Assert.False(gate.TryAdmit(Key(0), at(11.5f, 0f), 2f));
    Assert.True(gate.TryAdmit(Key(0), at(12.5f, 0f), 2f));
    Assert.True(gate.TryAdmit(Key(1), at(0f, 0f), 2f));
  }

  // Fails when strain moves the gate by a different step than 1 %, or not at all.
  [Fact]
  public void Reading_GateWaitsForOnePercentStrain() {
    var gate = new MpReadingGate();
    var at = (float strain) =>
      new MpSegmentReading(10f, 100f, strain, 1f, MpShaftSize.Line);

    Assert.True(gate.TryAdmit(Key(0), at(0f), 0f));
    Assert.False(gate.TryAdmit(Key(0), at(0.009f), 2f));
    Assert.True(gate.TryAdmit(Key(0), at(0.011f), 2f));
  }

  // Fails when a segment removed from the run stays remembered by the gate.
  [Fact]
  public void Reading_GateForgetsRemovedSegments() {
    var gate = new MpReadingGate();
    var reading = new MpSegmentReading(10f, 100f, 0f, 1f, MpShaftSize.Line);
    Assert.True(gate.TryAdmit(Key(0), reading, 0f));
    Assert.True(gate.TryAdmit(Key(1), reading, 0f));

    gate.Retain(new HashSet<NodeKey> { Key(1) });

    Assert.True(gate.TryAdmit(Key(0), reading, 0.1f));
    Assert.False(gate.TryAdmit(Key(1), reading, 0.1f));
  }

  [Fact]
  public void Reading_RoundTripsThroughTheTree() {
    var tree = new TreeAttribute();
    var reading = new MpSegmentReading(12f, 80f, 0.25f, -3f, MpShaftSize.Main);

    reading.WriteTo(tree);

    Assert.True(MpSegmentReading.TryReadFrom(tree, out var read));
    Assert.Equal(reading, read);
    Assert.False(MpSegmentReading.TryReadFrom(new TreeAttribute(), out _));
  }

  // Fails when the network writes every tick or never hands the reading to the segment.
  [Fact]
  public void Reading_NetworkWritesThroughTheGate() {
    var w = new TestWorld();
    var segment = new SegmentNode();
    var net = new MpShaftNetwork(w.Networks);
    var pos = new BlockPos(0, 0, 0);
    w.Place(
      pos,
      TestBlocks.Configure(new Block(), "test:mpshaft1", 1),
      segment
    );
    net.Nodes.Add(pos);
    net.NodeKeys.Add(NodeKey.Centre(pos));

    for (int i = 0; i < 4; i++)
      net.OnTick(w.Accessor, 0.2f, w.Networks);
    Assert.Equal(1, segment.Stores);

    segment.Strain = 0.05f;
    net.OnTick(w.Accessor, 0.2f, w.Networks);
    Assert.Equal(1, segment.Stores);
    net.OnTick(w.Accessor, 0.2f, w.Networks);
    Assert.Equal(2, segment.Stores);
    Assert.Equal(0.05f, segment.Stored!.Value.Strain);
    Assert.Equal(100f, segment.Stored.Value.RatingNm);
  }

  #endregion

  #region Standing and ratings

  // Fails when a node's standing torque is dropped from the run's resistance or applied per run instead of per node.
  [Fact]
  public void Friction_FortyMainCellsHoldUnderThirteenHundred() {
    var held = Chain(40, 0f, out var n, 1f);
    for (int i = 0; i < 40; i++)
      held.SetStanding(n[i], 32.5f);
    held.SetDrive(n[0], 1299f);
    held.Step(0.2f, 100f);

    var turning = Chain(40, 0f, out var m, 1f);
    for (int i = 0; i < 40; i++)
      turning.SetStanding(m[i], 32.5f);
    turning.SetDrive(m[0], 1301f);
    turning.Step(0.2f, 100f);

    Assert.Equal(0f, held.Speed(0));
    Assert.True(turning.Speed(0) > 0f);
  }

  // Fails when standing torque is scaled by speed instead of staying constant.
  [Fact]
  public void Friction_ConstantWithSpeed() {
    var slow = Chain(1, 5f, out var a);
    var fast = Chain(1, 50f, out var b);
    slow.SetStanding(a[0], 40f);
    fast.SetStanding(b[0], 40f);
    slow.SetDrive(a[0], 100f);
    fast.SetDrive(b[0], 100f);

    slow.Step(0.2f, 100f);
    slow.SolveTorquePath();
    fast.Step(0.2f, 100f);
    fast.SolveTorquePath();

    Assert.Equal(60f, slow.NetTorque(a[0]), 3);
    Assert.Equal(60f, fast.NetTorque(b[0]), 3);
  }

  // Fails when a geared joint is rated against the lowest rating at the reference speed instead of each end's torque over its own rating.
  [Fact]
  public void Rating_GearedJointGoesByWorstEndRatio() {
    var store = new MpShaftStore();
    int a = store.AddNode(Key(0), store.AddGroup(10f), 0f, 0f);
    int b = store.AddNode(Key(1), store.AddGroup(10f), 0f, 0f);
    Assert.True(store.TryAddEdge(a, b, 2f, 1));
    store.SetRating(a, 150f);
    store.SetRating(b, 250f);
    store.SetDrive(a, 100f);
    store.SetLoad(b, 200f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(200f / 250f, store.EdgeLoad(0), 3);
  }

  // Fails when a node is rated by the weakest rating on its line instead of its own.
  [Fact]
  public void Rating_NodeLoadFollowsItsOwnRating() {
    var store = Chain(3, 10f, out var n);
    store.SetRating(n[0], 200f);
    store.SetRating(n[1], 100f);
    store.SetRating(n[2], 400f);
    store.SetDrive(n[0], 90f);
    store.SetLoad(n[2], 90f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(0.45f, store.NodeLoad(n[0]), 3);
    Assert.Equal(0.9f, store.NodeLoad(n[1]), 3);
    Assert.Equal(0.225f, store.NodeLoad(n[2]), 3);
  }

  // Fails when an unrated end counts as rated zero and so reads as infinitely loaded.
  [Fact]
  public void Rating_UnratedEndDoesNotCount() {
    var store = Chain(2, 10f, out var n);
    store.SetRating(n[1], 200f);
    store.SetDrive(n[0], 50f);
    store.SetLoad(n[1], 50f);

    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    Assert.Equal(0f, store.NodeLoad(n[0]));
    Assert.Equal(0.25f, store.EdgeLoad(0), 3);
  }

  #endregion

  [Fact]
  public void Bench_5000Cells() {
    const int cells = 5000;
    var store = Chain(cells, 10f, out var n);
    store.SetDrive(n[0], 1000f);
    store.SetLoad(n[cells - 1], 1000f);
    const int steps = 200;
    store.Step(0.2f, 100f);
    store.SolveTorquePath();

    var clock = Stopwatch.StartNew();
    for (int i = 0; i < steps; i++) {
      store.Step(0.2f, 100f);
      store.SolveTorquePath();
    }
    clock.Stop();

    output.WriteLine(
      $"Bench_5000Cells: {clock.Elapsed.TotalMilliseconds / steps:F3} ms per step"
    );
    Assert.Equal(1000f, store.EdgeTorque(cells / 2), 2);
  }
}
