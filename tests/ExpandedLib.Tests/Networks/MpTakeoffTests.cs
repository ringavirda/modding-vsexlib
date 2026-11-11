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

  // Fails when the hold term is read in the network's frame (speed / g): the link's axle then turns at g times the held speed and the iron pays a different power than vanilla takes.
  [Theory]
  [InlineData(5.5f, 0.3f)]
  [InlineData(3f, 0.3f)]
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
      late[i] = network.Speed * geared;
    }

    Assert.InRange(late.Min(), 0.296f, 0.302f);
    Assert.InRange(late.Max(), 0.296f, 0.302f);
    Assert.Equal(
      network.NetworkResistance / geared * 7000f / 3f,
      link.TorqueTakenNm,
      2f
    );
    float vanillaW =
      network.NetworkResistance * MathF.Abs(network.Speed) * 7000f * 5f;
    Assert.Equal(vanillaW, link.TorqueTakenNm * 4.48f, vanillaW * 0.01f);
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

  // Fails when the lower clamp of the wanted torque is dropped: with 0.5 given and a wanted torque of -0.3 the blend gives 0.10, not 0.25.
  [Fact]
  public void A_negative_wanted_torque_is_clamped_to_zero_before_the_blend() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    network.NetworkTorque = 0.8f;
    ReflectionHelpers.SetField(link, "_given", 0.5f);

    Assert.Equal(0.25f, link.GetTorque(0, link.HeldSpeed, out _), 1e-4f);
  }

  // Fails when a client compares the signed speed: a link discovered from the other end never reads as overrun on a client.
  [Fact]
  public void A_client_reads_an_overrun_on_the_unsigned_network_speed() {
    var link = Link(MechPower.Network(0.5f, 0.3f), 4.48f);
    ReflectionHelpers.SetField(link, "propagationDir", BlockFacing.SOUTH);
    link.SideOverride = EnumAppSide.Client;

    Assert.True(link.Overrunning);
  }

  // Fails when the overrun is compared in the network's frame: behind a large gear a network at 0.1 puts the axle at 0.55, past the held 0.299.
  [Fact]
  public void An_overrun_is_judged_on_the_links_own_axle() {
    var link = Link(MechPower.Network(0.1f, 0.3f), 4.48f, 5.5f);

    Assert.True(link.Overrunning);
  }

  // Fails when the block body is left to vanilla's renderer: the megablock is then drawn a second time, turning on the axle.
  [Fact]
  public void The_link_gives_vanilla_no_shape_to_render() {
    var link = new BEBehaviorMpTakeoff(new Host());

    var shape = typeof(BEBehaviorMpTakeoff)
      .GetMethod(
        "GetShape",
        System.Reflection.BindingFlags.NonPublic
          | System.Reflection.BindingFlags.Instance
      )!
      .Invoke(link, []);

    Assert.Null(shape);
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

  // Fails when the iron speed or the torque given is not saved: a client's block info reads 0, and a link on a block entity of its own starts at 0 after a chunk load.
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

  // Fails when the window is a literal that is not two iron intervals of 200 ms in vanilla's 100 ms steps (the old 6), or one step short: the link reads stale
  // at the fifth step without a load, or not yet at the fourth.
  [Fact]
  public void An_iron_run_that_has_not_loaded_the_link_for_two_intervals_holds_nothing() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    link.SideOverride = EnumAppSide.Server;
    Join(network, 1, resistance: 0.3f);
    for (int i = 0; i < 4; i++)
      link.GetTorque(i, network.Speed, out _);
    Assert.True(link.HeldSpeed > 0f);
    link.GetTorque(0, network.Speed, out _);
    Assert.Equal(0f, link.HeldSpeed);
    link.LoadTorque(4.48f);
    Assert.True(link.HeldSpeed > 0f);

    for (int i = 0; i < 5; i++)
      link.GetTorque(i, network.Speed, out _);

    Assert.Equal(0f, link.HeldSpeed);
    Assert.Equal(0f, link.GetTorque(0, network.Speed, out _));
    Assert.Equal(0f, link.TorqueTakenNm);
  }

  // Fails when the window falls under one iron interval: a run that loads the link every second vanilla step then reads stale and hands no torque.
  [Fact]
  public void A_link_loaded_every_iron_interval_never_reads_stale_over_two_seconds() {
    var network = new MechanicalNetwork();
    var link = Link(network, 4.48f);
    link.SideOverride = EnumAppSide.Server;
    Join(network, 1, resistance: 0.3f);

    for (int i = 0; i < 20; i++) {
      if (i % 2 == 0)
        link.LoadTorque(4.48f);
      float given = link.GetTorque(i, network.Speed, out _);
      Assert.True(link.HeldSpeed > 0f, $"step {i}");
      if (i > 5)
        Assert.True(given > 0f, $"step {i}");
    }
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
