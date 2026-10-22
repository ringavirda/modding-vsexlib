using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The friction links of an "mpshaft" run, the clutch and the belt, as the run slips, locks and wears them:
/// <see cref="BEBehaviorMpClutch"/>, <see cref="BEBehaviorMpBelt"/> and <see cref="MpShaftNetwork"/>.
/// </summary>
public class MpLinkTests {
  private sealed class Cell(float inertia)
    : BlockEntity,
      IMpEnergyStorage,
      IMpEnergyProducer {
    public float Drive { get; set; }

    public float Inertia => inertia;

    public float DriveTorque(float speed) => Drive;
  }

  private sealed class Host : BlockEntity;

  private sealed class CellMember : BEBehaviorNetworkMember {
    public CellMember(BlockEntity be, string connectors)
      : base(be) {
      NetworkType = "mpshaft";
      DeclareConnectors(connectors);
    }
  }

  private sealed class LinkMember : BEBehaviorNetworkMember, INetworkMember {
    public LinkMember(BlockEntity be)
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

  private static readonly BlockPos A = new(0, 0, 0);
  private static readonly BlockPos F = new(1, 0, 0);
  private static readonly BlockPos D = new(1, 0, 1);

  private static int _ids = 700;

  private static void Put(
    TestWorld w,
    BlockPos pos,
    BlockEntity be,
    BEBehaviorNetworkMember member
  ) {
    be.Behaviors.Add(member);
    int id = _ids++;
    w.Place(pos, TestBlocks.Configure(new Block(), $"test:link{id}", id), be);
    w.Initialize(be);
  }

  /// <summary>A driven cell at A and a free cell at D, joined through the host at F that holds whatever link the caller fits.</summary>
  private static (TestWorld World, Host Host) Line(float drive) {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    var a = new Cell(100f) { Drive = drive };
    var d = new Cell(100f);
    var host = new Host();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, host, new LinkMember(host));
    Put(w, D, d, new CellMember(d, "wn"));
    return (w, host);
  }

  private static MpShaftNetwork Run(TestWorld w) =>
    (MpShaftNetwork)w.NetworkAt(A)!;

  private static void Tick(TestWorld w, double hours = 0) {
    w.AdvanceHours(hours);
    Run(w).OnTick(w.Accessor, 0.2f, w.Networks);
  }

  private static float SpeedAt(TestWorld w, BlockPos pos) {
    var store = Run(w).Store;
    return store.NodeSpeed(store.IndexOf(NodeKey.Centre(pos)));
  }

  private static BEBehaviorMpClutch Clutch(Host host) {
    var clutch = new BEBehaviorMpClutch(host);
    clutch.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), MpShaftSize.Main);
    host.Behaviors.Add(clutch);
    return clutch;
  }

  private static BEBehaviorMpBelt Belt(Host host) {
    var belt = new BEBehaviorMpBelt(host);
    belt.Connect(
      NodeKey.Port(F, 0),
      NodeKey.Port(F, 1),
      MpShaftSize.Line,
      MpShaftSize.Line
    );
    host.Behaviors.Add(belt);
    return belt;
  }

  #region Clutch

  // Fails when a released clutch still joins its two sides.
  [Fact]
  public void Clutch_ReleasedJoinsNothing() {
    var (w, host) = Line(1000f);
    Clutch(host);

    for (int i = 0; i < 20; i++)
      Tick(w);

    Assert.True(SpeedAt(w, A) > 10f);
    Assert.Equal(0f, SpeedAt(w, D), 3);
  }

  // Fails when the clamp starts at its cap, so the driven side is dragged along on the first step.
  [Fact]
  public void Clutch_ClampComesOnFromNothing() {
    var (w, host) = Line(1000f);
    var clutch = Clutch(host);
    Tick(w);
    clutch.Engaged = true;

    Tick(w);

    Assert.True(SpeedAt(w, A) > 1f);
    Assert.Equal(0f, SpeedAt(w, D), 2);
  }

  // Fails when a slipping clutch carries nothing, or the engaged clutch never locks.
  [Fact]
  public void Clutch_SlipsThenLocksWhenTheSidesMeet() {
    var (w, host) = Line(1000f);
    var clutch = Clutch(host);
    Tick(w);
    clutch.Engaged = true;

    for (int i = 0; i < 2; i++)
      Tick(w);
    float drivenEarly = SpeedAt(w, D);
    float driverEarly = SpeedAt(w, A);
    for (int i = 0; i < 60; i++)
      Tick(w);

    Assert.True(drivenEarly > 0f);
    Assert.True(drivenEarly < driverEarly);
    Assert.Equal(SpeedAt(w, A), SpeedAt(w, D), 1);
    Assert.True(SpeedAt(w, D) > driverEarly);
    Assert.Equal(1000f * 0.2f * 63 / 200f, SpeedAt(w, D), 1f);
  }

  #endregion

  #region Belt

  // Fails when the slipping belt carries other than 0.8 of its cap: a 390 N*m cap carries 312, which turns a free 100 kg*m^2 side by 0.62 rad/s in a step.
  [Fact]
  public void Belt_SlipsAtPointEightOfCap() {
    var (w, host) = Line(1000f);
    Belt(host);

    Tick(w);

    Assert.Equal(312f * 0.2f / 100f, SpeedAt(w, D), 0.05f);
    Assert.True(SpeedAt(w, A) > SpeedAt(w, D));
  }

  // Fails when slipping does not wear the belt, or a worn-out belt stays on and keeps carrying.
  [Fact]
  public void Belt_WearsAndBreaksAtOneDayOfSlip() {
    var (w, host) = Line(1000f);
    var belt = Belt(host);
    Run(w).FailureOverride = true;
    int snaps = 0;
    belt.Snapped += () => snaps++;
    Tick(w);
    Tick(w);

    Tick(w, 1);

    Assert.InRange(belt.Wear, 0.0001f, 0.99f);
    Assert.False(belt.Broken);

    Tick(w, 48);

    Assert.Equal(1f, belt.Wear);
    Assert.True(belt.Broken);
    Assert.False(belt.Engaged);
    Assert.Equal(1, snaps);
    float driven = SpeedAt(w, D);
    Tick(w);
    Assert.Equal(driven, SpeedAt(w, D), 3);
  }

  #endregion
}
