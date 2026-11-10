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
    float ironSpeed
  ) {
    var host = new Host { Pos = new BlockPos(0, 0, 0) };
    var link = MechPower.Attach(host, new BEBehaviorMpTakeoff(host), network);
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
    float loaded = IronSpeedWith(resistance: 0.1f, ironNetworkSpeed: 0f);
    float free = IronSpeedWith(resistance: 0f, ironNetworkSpeed: 0f);

    Assert.Equal(4f, free, 0.05f);
    Assert.Equal((2000f - 0.1f * 7000f / 3f) / 500f, loaded, 0.05f);
  }

  // Fails when a vanilla producer's overrun still loads the iron run or drives it: the run then settles below its free speed.
  [Fact]
  public void A_faster_vanilla_producer_overruns_and_leaves_the_iron_run_alone() {
    float overrun = IronSpeedWith(resistance: 0.1f, ironNetworkSpeed: 3f);
    float free = IronSpeedWith(resistance: 0f, ironNetworkSpeed: 0f);

    Assert.Equal(free, overrun, 0.01f);
  }

  // Fails when the overrun test is dropped from the torque the link gives: vanilla then has torque added to a network that is already faster.
  [Fact]
  public void An_overrun_link_gives_no_torque_and_takes_no_load() {
    var network = new MechanicalNetwork();
    var link = Link(network, 2.24f);
    Join(network, 1, resistance: 0.3f, torque: 0.35f);
    Steps(network, 200);

    Assert.True(
      network.Speed > link.HeldSpeed + BEBehaviorMpTakeoff.OverrunBand
    );
    Assert.True(link.Overrunning);
    Assert.Equal(0f, link.GetTorque(0, network.Speed, out _));
    Assert.Equal(0f, link.LoadTorque(2.24f));
  }

  // Fails when the overrun test is dropped and the proportional term alone is left to ease the torque off: just past the band it still gives nearly the resistance.
  [Fact]
  public void A_link_just_past_its_overrun_band_gives_no_torque() {
    var link = Link(MechPower.Network(0.16f, 0.3f), 2.24f);

    Assert.True(link.Overrunning);
    Assert.Equal(0f, link.GetTorque(0, 0.16f, out _));
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
  /// on its far cell over a fixed vanilla network of the given resistance (units) and speed (vanilla units).</summary>
  private static float IronSpeedWith(float resistance, float ironNetworkSpeed) {
    var w = new TestWorld();
    using (w) {
      w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
      var driver = new Droop();
      var host = new Host();
      var a = new BlockPos(0, 0, 0);
      var f = new BlockPos(1, 0, 0);
      Put(w, a, driver, new CellMember(driver, "e"), 5301);
      Put(w, f, host, new CellMember(host, "w"), 5302);
      MechPower.Attach(
        host,
        new BEBehaviorMpTakeoff(host),
        MechPower.Network(ironNetworkSpeed, resistance)
      );
      var net = (MpShaftNetwork)w.NetworkAt(a)!;
      for (int i = 0; i < 400; i++)
        net.OnTick(w.Accessor, 0.2f, w.Networks);
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
