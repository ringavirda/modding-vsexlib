using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The angle of an "mpshaft" run: kept on the group, carried by every store operation, sent by the network on
/// its cadence, and shown by every cell of a client in step.
/// </summary>
public class MpShaftTurningTests {
  private static NodeKey Key(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private static readonly BlockPos Anchor = new(5, 6, 7);

  private static MpShaftStore Spinning(
    int nodes,
    float speed,
    params int[] ratios
  ) {
    var store = new MpShaftStore();
    int group = store.AddGroup(speed);
    for (int i = 0; i < nodes; i++)
      store.AddNode(Key(i), group, 1f, 0f, i < ratios.Length ? ratios[i] : 1f);
    return store;
  }

  #region Store

  // Fails when a group's angle advances by its new speed instead of the mean of the speeds either side of the step.
  [Fact]
  public void A_group_turns_by_the_mean_of_its_speeds_over_the_step() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int node = store.AddNode(Key(0), group, 1f, 0f);
    store.SetDrive(node, 1f);

    store.Step(1f, 100f);

    Assert.Equal(1f, store.Speed(group), 4);
    Assert.Equal(0.5d, store.Angle(group), 6);
  }

  // Fails when a late cell keeps an angle of its own once it joins the run: the late cells are made with
  // the angle set to 0 and a join keeps the joining group's angle.
  [Fact]
  public void Five_cells_joined_in_two_parts_read_one_angle() {
    var store = Spinning(2, 2f);
    store.Step(1f, 100f);
    double before = store.NodeAngle(0);
    Assert.Equal(2d, before, 6);

    for (int x = 2; x < 5; x++)
      store.AddNode(Key(x), store.AddGroup(), 1f, 0f);
    Assert.True(
      store.TryAddEdge(store.IndexOf(Key(2)), store.IndexOf(Key(1)), 1f, 1)
    );
    Assert.True(
      store.TryAddEdge(store.IndexOf(Key(1)), store.IndexOf(Key(3)), 1f, 1)
    );
    Assert.True(
      store.TryAddEdge(store.IndexOf(Key(3)), store.IndexOf(Key(4)), 1f, 1)
    );

    Assert.Equal(before, store.NodeAngle(store.IndexOf(Key(0))), 5);
    store.Step(1f, 100f);
    double shown = store.NodeAngle(store.IndexOf(Key(0)));
    for (int x = 1; x < 5; x++)
      Assert.Equal(shown, store.NodeAngle(store.IndexOf(Key(x))), 5);
    Assert.True(shown > before);
  }

  // Fails when Take makes the groups it copies without the angle of their source.
  [Fact]
  public void Take_and_Clone_carry_each_groups_angle() {
    var store = Spinning(2, 2f);
    store.Step(1f, 100f);

    var copy = store.Clone();
    var part = new MpShaftStore();
    part.Take(store, new HashSet<NodeKey> { Key(1) });

    Assert.Equal(2d, copy.NodeAngle(copy.IndexOf(Key(0))), 6);
    Assert.Equal(2d, part.NodeAngle(part.IndexOf(Key(1))), 6);
  }

  // Fails when TakeLoose gives a node its group's angle instead of the angle the node showed.
  [Fact]
  public void TakeLoose_keeps_the_angle_each_node_showed() {
    var store = Spinning(2, 2f, 1, -2);
    store.Step(1f, 100f);
    Assert.Equal(-4d, store.NodeAngle(1), 6);

    var loose = new MpShaftStore();
    loose.TakeLoose(store, new HashSet<NodeKey> { Key(0), Key(1) });

    Assert.Equal(2d, loose.NodeAngle(loose.IndexOf(Key(0))), 6);
    Assert.Equal(-4d, loose.NodeAngle(loose.IndexOf(Key(1))), 6);
  }

  // Fails when Settle's renumbering of the groups drops their angles, or a node folded into group 0 keeps its own.
  [Fact]
  public void Settle_keeps_the_angle_of_the_group_that_takes_a_lone_node() {
    var store = Spinning(1, 2f);
    store.Step(1f, 100f);
    store.AddNode(Key(5), store.AddGroup(), 1f, 0f);

    store.Settle();

    Assert.Equal(2d, store.NodeAngle(store.IndexOf(Key(0))), 6);
    Assert.Equal(2d, store.NodeAngle(store.IndexOf(Key(5))), 6);
  }

  // Fails when Absorb overwrites the angle of the group that takes the nodes, or leaves it unset when the taking group has none.
  [Fact]
  public void Absorb_keeps_the_taking_groups_angle_unless_it_has_none() {
    var turning = Spinning(1, 2f);
    turning.Step(1f, 100f);
    var other = new MpShaftStore();
    other.AddNode(Key(7), other.AddGroup(1f), 1f, 0f);
    other.Step(3f, 100f);
    var fresh = new MpShaftStore();
    fresh.AddNode(Key(8), fresh.AddGroup(), 1f, 0f);

    turning.Absorb(other);
    fresh.Absorb(other);

    Assert.Equal(2d, turning.NodeAngle(turning.IndexOf(Key(7))), 6);
    Assert.Equal(3d, fresh.NodeAngle(fresh.IndexOf(Key(7))), 6);
  }

  #endregion

  #region Client

  // Fails when AngleRad reads an angle kept by the behavior itself from its load instead of its run's.
  [Fact]
  public void A_cell_loaded_late_reads_its_runs_angle() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var first = TurningFixtures.Stand(world, block, new BlockPos(0, 64, 0));
    TurningFixtures.NameRun(first, Anchor);
    TurningFixtures
      .Angles(world)
      .Receive(TurningFixtures.Packet(Anchor, 1.0, 0f), 0);
    var late = TurningFixtures.Stand(world, block, new BlockPos(1, 64, 0));
    TurningFixtures.NameRun(late, Anchor, 2f);

    Assert.Equal(1.0f, TurningFixtures.Behavior(first).AngleRad(0), 4);
    Assert.Equal(2.0f, TurningFixtures.Behavior(late).AngleRad(0), 4);
  }

  // Fails when the Before stage does not advance the client's run angles.
  [Fact]
  public void Between_packets_the_angle_runs_on_at_the_packet_speed() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var twin = TurningFixtures.Stand(world, block, new BlockPos(0, 64, 0));
    TurningFixtures.NameRun(twin, Anchor);
    TurningFixtures
      .Angles(world)
      .Receive(TurningFixtures.Packet(Anchor, 1.0, 2f), 0);

    twin.Frames(0.1f, 5);

    Assert.Equal(2.0f, TurningFixtures.Behavior(twin).AngleRad(0), 3);
  }

  // Fails when a run with no packet for ten seconds keeps turning.
  [Fact]
  public void A_run_silent_for_ten_seconds_stops() {
    var angles = new MpRunAngles();
    angles.Receive(TurningFixtures.Packet(Anchor, 1.0, 2f), 0);

    angles.Advance(0.1f, 10_001);
    angles.TryGet(new NodeKey(Anchor, 0), out var run);

    Assert.Equal(0f, run.Speed);
    Assert.Equal(1.0, run.Angle, 6);
  }

  // Fails when a gap over a quarter turn is eased instead of snapped, or one under it snapped instead of eased.
  [Fact]
  public void A_far_gap_snaps_and_a_near_one_closes_over_half_a_second() {
    var angles = new MpRunAngles();
    var key = new NodeKey(Anchor, 0);
    angles.Receive(TurningFixtures.Packet(Anchor, 0.0, 0f), 0);
    angles.Receive(TurningFixtures.Packet(Anchor, 3.0, 0f), 0);

    angles.Advance(0.01f, 0);
    angles.TryGet(key, out var run);
    Assert.Equal(3.0, run.Angle, 6);

    angles.Receive(TurningFixtures.Packet(Anchor, 3.5, 0f), 0);
    angles.Advance(0.25f, 0);
    Assert.Equal(3.25, run.Angle, 6);
  }

  #endregion

  #region Network

  private sealed class Cell : BlockEntity, IMpEnergyStorage {
    public float Inertia { get; set; } = 10f;
  }

  private sealed class Motor : BlockEntity, IMpEnergyProducer {
    public float Torque { get; set; }

    public float DriveTorque(float speed) => Torque;
  }

  private const int SendZ = 777;

  private static MpShaftNetwork Wire(
    TestWorld world,
    params (BlockPos Pos, BlockEntity Be)[] nodes
  ) {
    var net = new MpShaftNetwork(world.Networks);
    int id = 100;
    foreach (var (pos, be) in nodes) {
      world.Place(
        pos,
        TestBlocks.Configure(new Block(), $"test:mpturn{id}", id),
        be
      );
      be.Behaviors.Add(new BEBehaviorTurningParts(be));
      world.Initialize(be);
      net.Nodes.Add(pos);
      net.NodeKeys.Add(NodeKey.Centre(pos));
      id++;
    }
    return net;
  }

  private static List<MpRunAnglePacket> Capture() {
    var sent = new List<MpRunAnglePacket>();
    MpRunAngles.SetServerSend(p => {
      if (p.Z.Contains(SendZ))
        sent.Add(p);
    });
    return sent;
  }

  private static (TestWorld World, MpShaftNetwork Net, Motor Motor) Steady(
    float torque = 0f
  ) {
    var world = new TestWorld();
    var motor = new Motor { Torque = torque };
    var net = Wire(
      world,
      (new BlockPos(2, 0, SendZ), new Cell()),
      (new BlockPos(1, 0, SendZ), motor),
      (new BlockPos(3, 0, SendZ), new Cell())
    );
    int group = net.Store.AddGroup(3f);
    foreach (var key in net.NodeKeys.OrderBy(k => k.Pos.X))
      net.Store.AddNode(key, group, 10f, 0f);
    return (world, net, motor);
  }

  private static void Tick(TestWorld world, MpShaftNetwork net, int times) {
    for (int i = 0; i < times; i++)
      net.OnTick(world.Accessor, 0.2f, world.Networks);
  }

  // Fails when a run is sent on every tick instead of every fifth tick while its speed holds.
  [Fact]
  public void A_steady_run_is_sent_every_fifth_tick() {
    var sent = Capture();
    var (world, net, _) = Steady();
    using (world) {
      Tick(world, net, 6);
    }
    MpRunAngles.SetServerSend(null);

    Assert.Equal(2, sent.Count);
    Assert.Equal(3f, sent[0].Speed[0], 3);
    Assert.Equal(0.6d, sent[0].Angle[0], 4);
  }

  // Fails when a run whose speed moves by more than 2 % waits for the cadence.
  [Fact]
  public void A_run_that_is_speeding_up_is_sent_every_tick() {
    var sent = Capture();
    var (world, net, _) = Steady(50f);
    using (world) {
      Tick(world, net, 3);
    }
    MpRunAngles.SetServerSend(null);

    Assert.Equal(3, sent.Count);
    Assert.True(sent[2].Speed[0] > sent[0].Speed[0]);
  }

  // Fails when a suspended run is sent at its speed instead of 0, or only on the cadence.
  [Fact]
  public void A_suspended_run_is_sent_stopped_at_its_held_angle_at_once() {
    var sent = Capture();
    var (world, net, _) = Steady();
    using (world) {
      Tick(world, net, 2);
      double held = net.Store.Angle(0);
      world.UnloadChunkAt(new BlockPos(2, 0, SendZ));
      Tick(world, net, 1);

      Assert.Equal(2, sent.Count);
      Assert.Equal(0f, sent[1].Speed[0]);
      Assert.Equal(held, sent[1].Angle[0], 6);
    }
    MpRunAngles.SetServerSend(null);
  }

  // Fails when the anchor is the greatest node key instead of the least, or a cell is told no run.
  [Fact]
  public void A_run_is_named_by_its_least_node_key_and_each_cell_is_told_it() {
    var sent = Capture();
    var (world, net, _) = Steady();
    using (world) {
      Tick(world, net, 1);
      var tree = new TreeAttribute();
      world.GetBlockEntity(new BlockPos(3, 0, SendZ))!.ToTreeAttributes(tree);

      var run = ((ITreeAttribute)tree["turnRun"]).GetTreeAttribute("s0");
      Assert.Equal(1, run.GetInt("x"));
      Assert.Equal(SendZ, run.GetInt("z"));
      Assert.Equal(1, sent[0].X[0]);
    }
    MpRunAngles.SetServerSend(null);
  }

  #endregion
}
