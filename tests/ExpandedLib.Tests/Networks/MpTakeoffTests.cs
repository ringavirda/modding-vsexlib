using System;
using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The iron-to-vanilla link, <see cref="BEBehaviorMpTakeoff"/>: the vanilla network's speed and resistance
/// against a real <see cref="MechanicalNetwork"/> step, and the iron run's load on a real <see cref="MpShaftNetwork"/>.</summary>
public class MpTakeoffTests {
  private sealed class Host : BlockEntity;

  private sealed class VanillaNode(
    BlockEntity be,
    float resistance,
    float torque
  ) : BEBehaviorMPBase(be) {
    public override float GetResistance() => resistance;

    public override float GetTorque(
      long tick,
      float speed,
      out float resistance1
    ) {
      resistance1 = resistance;
      return torque;
    }
  }

  // A producer that gives torque in proportion to how far the network runs under its free speed, as a windmill does.
  private sealed class Wind(BlockEntity be, float resistance, float cap)
    : BEBehaviorMPBase(be) {
    public override float GetResistance() => resistance;

    public override float GetTorque(
      long tick,
      float speed,
      out float resistance1
    ) {
      resistance1 = resistance;
      return MathF.Max(0f, cap - speed);
    }
  }

  private sealed class Droop : BlockEntity, IMpEnergyStorage, IMpEnergyProducer {
    public float Inertia => 100f;

    public float DriveTorque(float speed) => 2000f - 500f * speed;

    public float DriveSlope(float speed) => -500f;
  }

  private sealed class CellMember : BEBehaviorNetworkMember {
    public CellMember(BlockEntity be, string connectors)
      : base(be) {
      NetworkType = "mpshaft";
      DeclareConnectors(connectors);
    }
  }

  private static BEBehaviorMpTakeoff Link(
    MechanicalNetwork network,
    float ironSpeed,
    float geared = 1f
  ) {
    var host = new Host { Pos = new BlockPos(0, 0, 0) };
    var link = MechPower.Attach(host, new BEBehaviorMpTakeoff(host), network);
    link.SetOrientations();
    link.GearedRatio = geared;
    network.Join(link);
    link.LoadTorque(ironSpeed);
    return link;
  }

  private static void Join(
    MechanicalNetwork network,
    int z,
    float resistance,
    float torque = 0f
  ) {
    var host = new Host { Pos = new BlockPos(0, 0, z) };
    network.Join(
      MechPower.Attach(host, new VanillaNode(host, resistance, torque), network)
    );
  }

  private static void Steps(MechanicalNetwork network, int count) {
    for (int i = 0; i < count; i++)
      network.updateNetwork(i);
  }

  // Fails when the ratio is dropped from the held speed: the network then turns at iron speed over 5 rad/s.
  [Theory]
  [InlineData(2.24f, 0.149f)]
  [InlineData(3.36f, 0.224f)]
  [InlineData(4.48f, 0.299f)]
  public void The_vanilla_network_is_held_at_iron_speed_over_ratio_and_five(
    float iron,
    float expected
  ) {
    var network = new MechanicalNetwork();
    var link = Link(network, iron);
    Join(network, 1, resistance: 0.3f);

    Steps(network, 400);

    Assert.Equal(expected, network.Speed, 0.002f);
    Assert.Equal(expected, link.HeldSpeed, 0.001f);
  }

  // Fails when the resistance sum is not carried over to the iron load: the 0.3 unit becomes 700 N*m at the main.
  [Fact]
  public void The_vanilla_resistance_sum_loads_the_iron_run_by_7000_over_the_ratio() {
    var network = new MechanicalNetwork();
    var link = Link(network, 2.24f);
    Join(network, 1, resistance: 0.3f);
    Steps(network, 400);

    float load = link.LoadTorque(2.24f);

    Assert.Equal(700f, load, 1f);
    Assert.Equal(700f, link.TorqueTakenNm, 1f);
  }

  // Fails when a link that is not a consumer node of the shaft model is read as none: the run then turns at its free speed.
  [Fact]
  public void The_iron_run_settles_where_its_drive_meets_the_load_the_link_takes() {
    float loaded = IronSpeedWith(resistance: 0.1f, producer: 0f);
    float free = IronSpeedWith(resistance: 0f, producer: 0f);

    Assert.Equal(4f, free, 0.05f);
    Assert.Equal((2000f - 0.1f * 7000f / 3f) / 500f, loaded, 0.05f);
  }

  // Fails when a vanilla producer's overrun still loads the iron run or drives it: the run then settles below its free speed.
  [Fact]
  public void A_faster_vanilla_producer_overruns_and_leaves_the_iron_run_alone() {
    float overrun = IronSpeedWith(resistance: 0.1f, producer: 0.35f);
    float free = IronSpeedWith(resistance: 0f, producer: 0f);

    Assert.Equal(free, overrun, 0.01f);
  }

  // Fails when the overrun test is dropped from the torque the link gives: vanilla then has torque added to a network that is already faster.
  [Fact]
  public void An_overrun_link_gives_no_torque_and_takes_no_load() {
    var network = new MechanicalNetwork();
    var link = Link(network, 2.24f);
    Join(network, 1, resistance: 0.3f, torque: 0.35f);
    Steps(network, 200);

    Assert.True(network.Speed > link.HeldSpeed);
    Assert.True(link.Overrunning);
    Assert.Equal(0f, link.GetTorque(0, network.Speed, out _));
    Assert.Equal(0f, link.LoadTorque(2.24f));
  }

  // Fails when the link is read at ratio 1 (the held speed or the torque not taken over GearedRatio): the network then turns at 5.5 times the held speed or a fifth of it.
  [Theory]
  [InlineData(5.5f, 0.3f)]
  [InlineData(0.18181818f, 0.1f)]
  public void The_link_holds_its_axle_at_the_held_speed_behind_a_large_gear(
    float geared,
    float resistance
  ) {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f, geared);
    Join(network, 1, resistance);

    Steps(network, 600);
    float[] late = new float[100];
    for (int i = 0; i < late.Length; i++) {
      network.updateNetwork(1000 + i);
      late[i] = network.Speed;
    }

    Assert.InRange(late.Min(), 0.296f, 0.302f);
    Assert.InRange(late.Max(), 0.296f, 0.302f);
    Assert.Equal(
      network.NetworkResistance / geared * 7000f / 3f,
      link.TorqueTakenNm,
      2f
    );
  }

  // Fails when the link's own last torque is not taken out of the network's: it then gives its share twice and the speed passes the held one.
  [Fact]
  public void A_vanilla_producer_that_cannot_carry_the_load_alone_shares_it_without_chatter() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    var host = new Host { Pos = new BlockPos(0, 0, 1) };
    network.Join(MechPower.Attach(host, new Wind(host, 0.5f, 0.4f), network));
    Steps(network, 600);

    float lo = float.MaxValue,
      hi = float.MinValue,
      given = 0f;
    for (int i = 0; i < 100; i++) {
      network.updateNetwork(1000 + i);
      lo = MathF.Min(lo, network.Speed);
      hi = MathF.Max(hi, network.Speed);
      given = link.TorqueTakenNm;
    }

    Assert.InRange(lo, 0.296f, 0.302f);
    Assert.InRange(hi, 0.296f, 0.302f);
    float wind = 0.4f - 0.299f;
    Assert.Equal((network.NetworkResistance - wind) * 7000f / 3f, given, 30f);
  }

  // Fails when a producer that carries the load alone is not let overrun: the link keeps adding the hold gain on top of it.
  [Fact]
  public void A_producer_that_carries_the_load_alone_overruns_the_link_to_zero() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    var host = new Host { Pos = new BlockPos(0, 0, 1) };
    network.Join(MechPower.Attach(host, new Wind(host, 0.2f, 0.6f), network));
    Steps(network, 600);

    Assert.True(network.Speed > 0.35f);
    Assert.True(link.Overrunning);
    Assert.Equal(0f, link.TorqueTakenNm);
  }

  // Fails when the link's torque is not signed by its sense: placed against the discovery sense it pushes the network backward.
  [Fact]
  public void A_link_against_the_discovery_sense_drives_the_network_the_other_way() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    ReflectionHelpers.SetField(link, "propagationDir", BlockFacing.SOUTH);
    Join(network, 1, resistance: 0.3f);

    Steps(network, 600);

    Assert.Equal(-0.299f, network.Speed, 0.003f);
  }

  // Fails when a network driven against the link's sense is not given nothing: the link keeps billing the iron for torque that cannot turn it.
  [Fact]
  public void A_network_driven_backward_takes_and_gives_nothing() {
    var link = Link(MechPower.Network(-0.2f, 0.3f), 4.48f);

    Assert.Equal(0f, link.GetTorque(0, -0.2f, out _));
    Assert.Equal(0f, link.TorqueTakenNm);
    Assert.Equal(0f, link.LoadTorque(4.48f));
  }

  // Fails when the overrun is judged on the network's raw speed: a network turning against the link's sense then reads as overrun.
  [Fact]
  public void A_network_turning_against_the_links_sense_is_not_an_overrun() {
    var link = Link(MechPower.Network(0.5f, 0.3f), 4.48f);
    ReflectionHelpers.SetField(link, "propagationDir", BlockFacing.SOUTH);

    Assert.False(link.Overrunning);
    Assert.Equal(0f, link.GetTorque(0, 0.5f, out _));
  }

  // Fails when the iron speed or the torque given is not saved: a client's block info reads 0 and a chunk load starts the link at 0.
  [Fact]
  public void The_iron_speed_and_the_torque_given_are_saved_and_read_back() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    Join(network, 1, resistance: 0.3f);
    Steps(network, 400);
    var tree = new TreeAttribute();
    link.ToTreeAttributes(tree);

    var host = new Host { Pos = new BlockPos(0, 0, 0) };
    var loaded = MechPower.Attach(host, new BEBehaviorMpTakeoff(host), network);
    loaded.FromTreeAttributes(tree, new TestWorld().World);

    Assert.Equal(0.299f, loaded.HeldSpeed, 0.001f);
    Assert.Equal(link.TorqueTakenNm, loaded.TorqueTakenNm, 0.5f);
    Assert.True(loaded.TorqueTakenNm > 600f);
  }

  // Fails when the sync threshold is ignored: a change past it is never sent, or every tiny change is.
  [Fact]
  public void A_change_past_the_threshold_is_due_a_sync_and_a_small_one_is_not() {
    var link = Link(new MechanicalNetwork(), 4.48f);

    ReflectionHelpers.SetField(
      link,
      "_ironSpeed",
      4.48f + BEBehaviorMpTakeoff.SyncSpeedStep / 2f
    );
    Assert.False(link.SyncDue());
    ReflectionHelpers.SetField(
      link,
      "_ironSpeed",
      4.48f + BEBehaviorMpTakeoff.SyncSpeedStep * 2f
    );
    Assert.True(link.SyncDue());
    Assert.False(link.SyncDue());
    ReflectionHelpers.SetField(
      link,
      "_given",
      BEBehaviorMpTakeoff.SyncTorqueStep * 2f
    );
    Assert.True(link.SyncDue());
  }

  // Fails when a suspended iron run is not read as stopped: the link keeps driving the network at the last held speed with no load on the iron.
  [Fact]
  public void An_iron_run_that_has_not_ticked_for_two_intervals_holds_nothing() {
    long now = 1000;
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    link.Clock = () => now;
    link.LoadTorque(4.48f);
    Join(network, 1, resistance: 0.3f);
    Steps(network, 100);
    Assert.True(link.TorqueTakenNm > 0f);

    now += 2 * BlockNetworkModSystem.ListenerIntervalMs + 1;

    Assert.Equal(0f, link.HeldSpeed);
    Assert.Equal(0f, link.GetTorque(0, network.Speed, out _));
    Assert.Equal(0f, link.TorqueTakenNm);
  }

  // Fails when a stopped iron run still gives the vanilla side its resistance as torque or loads the run.
  [Fact]
  public void A_stopped_iron_run_gives_the_vanilla_side_nothing() {
    var network = new MechanicalNetwork();
    var link = Link(network, 0f);
    Join(network, 1, resistance: 0.3f);
    Steps(network, 5);

    Assert.Equal(0f, link.GetTorque(0, 0f, out float own));
    Assert.Equal(0f, own);
    Assert.Equal(0f, link.LoadTorque(0f));
    Assert.InRange(network.Speed, 0f, 1e-5f);
  }

  // Fails when the torque is not clamped to the rating: the network then reaches the held speed against a resistance the rating cannot carry.
  [Fact]
  public void Over_the_rating_the_vanilla_side_is_capped() {
    var over = new MechanicalNetwork();
    var capped = Link(over, 2.24f);
    Join(over, 1, resistance: 2f);
    var within = new MechanicalNetwork();
    Link(within, 2.24f);
    Join(within, 1, resistance: 1f);

    Steps(over, 400);
    Steps(within, 400);

    Assert.Equal(3280f / 7000f * 3f, capped.RatedUnits, 1e-3f);
    Assert.Equal(capped.RatedUnits, capped.GetTorque(0, 0f, out _), 1e-3f);
    Assert.Equal(3280f, capped.LoadTorque(2.24f), 1f);
    Assert.True(capped.AtRating);
    Assert.InRange(over.Speed, 0f, 0.001f);
    Assert.Equal(0.149f, within.Speed, 0.002f);
  }

  // Fails when a declaration is ignored: the ratio, the rating and the facings fall back to the take-off's.
  [Fact]
  public void A_declaration_sets_ratio_rating_and_facings() {
    var link = new BEBehaviorMpTakeoff(new Host());
    link.ReadProperties(
      new JsonObject(
        JObject.Parse(
          """{ "ratio": 4, "ratedNm": 1000, "facings": ["east", "west"] }"""
        )
      )
    );
    link.SetOrientations();

    Assert.Equal(4f, link.Ratio);
    Assert.Equal(1000f, link.RatedNm);
    Assert.Equal(BlockFacing.EAST, link.OutFacingForNetworkDiscovery);
    Assert.Equal([-1, 0, 0], link.AxisSign);
  }

  // Fails when an absent declaration reads as zero: the link takes ratio 3 and 3280 N*m, and meets north and south.
  [Fact]
  public void An_empty_declaration_is_the_take_off() {
    var link = new BEBehaviorMpTakeoff(new Host());
    link.ReadProperties(null);
    link.SetOrientations();

    Assert.Equal(3f, link.Ratio);
    Assert.Equal(3280f, link.RatedNm);
    Assert.Equal(BlockFacing.NORTH, link.OutFacingForNetworkDiscovery);
    Assert.Equal([0, 0, -1], link.AxisSign);
  }

  /// <summary>The speed (rad/s) a droop source of 2000 N*m at rest and 4 rad/s free settles an iron run at, a link
  /// on its far cell over a vanilla network, stepped once per iron tick, of the given resistance (units) and constant
  /// producer torque (units).</summary>
  private static float IronSpeedWith(float resistance, float producer) {
    var w = new TestWorld();
    using (w) {
      w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
      var driver = new Droop();
      var host = new Host();
      var a = new BlockPos(0, 0, 0);
      var f = new BlockPos(1, 0, 0);
      Put(w, a, driver, new CellMember(driver, "e"), 5301);
      Put(w, f, host, new CellMember(host, "w"), 5302);
      var vanilla = new MechanicalNetwork();
      var link = MechPower.Attach(host, new BEBehaviorMpTakeoff(host), vanilla);
      link.SetOrientations();
      vanilla.Join(link);
      Join(vanilla, 1, resistance, producer);
      var net = (MpShaftNetwork)w.NetworkAt(a)!;
      for (int i = 0; i < 400; i++) {
        net.OnTick(w.Accessor, 0.2f, w.Networks);
        vanilla.updateNetwork(i);
      }
      return net.Store.NodeSpeed(net.Store.IndexOf(NodeKey.Centre(a)));
    }
  }

  private static void Put(
    TestWorld w,
    BlockPos pos,
    BlockEntity be,
    BEBehaviorNetworkMember member,
    int id
  ) {
    be.Behaviors.Add(member);
    w.Place(
      pos,
      TestBlocks.Configure(new Block(), $"test:takeoff{id}", id),
      be
    );
    w.Initialize(be);
  }
}
