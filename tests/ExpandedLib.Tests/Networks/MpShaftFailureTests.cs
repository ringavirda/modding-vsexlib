using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Strain, parting and bursting on the "mpshaft" run, and the support rules of a straight run:
/// <see cref="MpShaftNetwork"/> and <see cref="MpShaftSpans"/>.
/// </summary>
public class MpShaftFailureTests {
  private sealed class DrivenSegment
    : BlockEntity,
      IMpEnergySegment,
      IMpEnergyStorage,
      IMpEnergyProducer {
    public float Torque { get; set; }

    public float Ultimate { get; set; }

    public int Parts { get; private set; }

    public float Inertia => 1f;

    public MpShaftSize Size => MpShaftSize.Line;

    public float RatingNm => 100f;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }

    public float UltimateNm => Ultimate;

    public float DriveTorque(float speed) => Torque;

    public void Part() => Parts++;
  }

  private sealed class LoadedSegment
    : BlockEntity,
      IMpEnergySegment,
      IMpEnergyStorage,
      IMpEnergyConsumer {
    public float Torque { get; set; }

    public float Inertia => 1f;

    public MpShaftSize Size => MpShaftSize.Line;

    public float RatingNm => 100f;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }

    public float LoadTorque(float speed) => Torque;
  }

  private sealed class Wheel : BlockEntity, IMpEnergyStorage, IMpEnergyRim {
    public float Inertia => 10f;

    public float RimLimitRadS { get; set; } = 10f;

    public float RimStrain { get; set; }

    public int Bursts { get; private set; }

    public void Burst() => Bursts++;
  }

  private static MpShaftNetwork Wire(
    TestWorld world,
    params (int X, BlockEntity Be)[] nodes
  ) {
    var net = new MpShaftNetwork(world.Networks);
    int id = 200;
    foreach (var (x, be) in nodes) {
      var pos = new BlockPos(x, 0, 0);
      world.Place(
        pos,
        TestBlocks.Configure(new Block(), $"test:mpfail{id++}", id),
        be
      );
      net.Nodes.Add(pos);
      net.NodeKeys.Add(NodeKey.Centre(pos));
    }
    return net;
  }

  private static void Tick(TestWorld w, MpShaftNetwork net, double hours) {
    w.AdvanceHours(hours);
    net.OnTick(w.Accessor, 0.2f, w.Networks);
  }

  /// <summary>Two joined segments held at rest by a drive of <paramref name="torque"/> N*m against an equal load, each rated 100 N*m.</summary>
  private static (
    TestWorld World,
    MpShaftNetwork Net,
    DrivenSegment Drive,
    LoadedSegment Load
  ) Pair(float torque, float ultimate = 0f) {
    var w = new TestWorld();
    var drive = new DrivenSegment { Torque = torque, Ultimate = ultimate };
    var load = new LoadedSegment { Torque = torque };
    var net = Wire(w, (0, drive), (1, load));
    Tick(w, net, 0);
    Assert.True(
      net.Store.TryAddEdge(
        net.Store.IndexOf(NodeKey.Centre(new BlockPos(0, 0, 0))),
        net.Store.IndexOf(NodeKey.Centre(new BlockPos(1, 0, 0))),
        1f,
        1
      )
    );
    return (w, net, drive, load);
  }

  #region Strain

  // Fails when strain accrues per tick instead of per in-game hour, or without the (T/R - 1) factor.
  [Fact]
  public void Strain_RisesWithOverloadPerGameHour() {
    var (w, net, drive, load) = Pair(200f);

    Tick(w, net, 0.25);

    Assert.Equal(0.25f, drive.Strain, 3);
    Assert.Equal(0.25f, load.Strain, 3);
  }

  // Fails when a segment at or under its rating takes strain.
  [Fact]
  public void Strain_UnderRatingStaysZero() {
    var (w, net, drive, _) = Pair(100f);

    Tick(w, net, 1);

    Assert.Equal(0f, drive.Strain);
  }

  // Fails when strain falls back once the overload is gone.
  [Fact]
  public void Strain_NeverRecovers() {
    var (w, net, drive, load) = Pair(200f);
    Tick(w, net, 0.25);

    drive.Torque = 10f;
    load.Torque = 10f;
    Tick(w, net, 0.5);

    Assert.Equal(0.25f, drive.Strain, 3);
  }

  // Fails when a segment parts below strain 1 or never parts at it.
  [Fact]
  public void Strain_PartsAtOne() {
    var (w, net, drive, _) = Pair(200f);

    Tick(w, net, 0.5);
    Assert.Equal(0, drive.Parts);
    Tick(w, net, 0.6);

    Assert.Equal(1f, drive.Strain);
    Assert.True(drive.Parts > 0);
  }

  // Fails when the ultimate is not an at-once limit independent of strain.
  [Fact]
  public void Strain_PartsAtOnceAtUltimate() {
    var (w, net, drive, load) = Pair(200f, 150f);

    Tick(w, net, 0.01);

    Assert.Equal(1f, drive.Strain);
    Assert.True(drive.Parts > 0);
    Assert.True(load.Strain < 1f);
  }

  // Fails when the server switch off still parts a segment.
  [Fact]
  public void Strain_FailureOffPartsNothing() {
    var (w, net, drive, _) = Pair(200f, 150f);
    net.FailureOverride = false;

    Tick(w, net, 2);

    Assert.Equal(1f, drive.Strain);
    Assert.Equal(0, drive.Parts);
  }

  #endregion

  #region Flywheel

  private static (TestWorld World, MpShaftNetwork Net, Wheel Wheel) Spinning(
    float speed
  ) {
    var w = new TestWorld();
    var wheel = new Wheel();
    var net = Wire(w, (0, wheel));
    net.Store.AddNode(
      NodeKey.Centre(new BlockPos(0, 0, 0)),
      net.Store.AddGroup(speed),
      wheel.Inertia,
      0f
    );
    Tick(w, net, 0);
    return (w, net, wheel);
  }

  // Fails when the rim takes no strain past its limit, takes it by a different law, or bursts before strain 1.
  [Fact]
  public void Flywheel_BurstsPastRimLimit() {
    var (w, net, wheel) = Spinning(15f);

    Tick(w, net, 1);
    Assert.Equal(0.5f, wheel.RimStrain, 2);
    Assert.Equal(0, wheel.Bursts);
    Tick(w, net, 1);

    Assert.Equal(1f, wheel.RimStrain);
    Assert.True(wheel.Bursts > 0);
  }

  // Fails when a block entity holding two nodes strains once per node instead of once per cell.
  [Fact]
  public void Flywheel_TwoNodesStrainTheCellOnce() {
    var w = new TestWorld();
    var wheel = new Wheel();
    var net = Wire(w, (0, wheel));
    var pos = new BlockPos(0, 0, 0);
    net.NodeKeys.Clear();
    int group = net.Store.AddGroup(15f);
    foreach (int port in new[] { 0, 1 }) {
      var key = NodeKey.Port(pos, port);
      net.NodeKeys.Add(key);
      net.Store.AddNode(key, group, wheel.Inertia, 0f);
    }
    Tick(w, net, 0);

    Tick(w, net, 0.5);

    Assert.Equal(0.25f, wheel.RimStrain, 2);
  }

  // Fails when a wheel under its limit strains.
  [Fact]
  public void Flywheel_UnderLimitTakesNoStrain() {
    var (w, net, wheel) = Spinning(9f);

    Tick(w, net, 5);

    Assert.Equal(0f, wheel.RimStrain);
    Assert.Equal(0, wheel.Bursts);
  }

  // Fails when the server switch off still bursts a wheel.
  [Fact]
  public void Flywheel_FailureOffNeverBursts() {
    var (w, net, wheel) = Spinning(30f);
    net.FailureOverride = false;

    Tick(w, net, 5);

    Assert.Equal(1f, wheel.RimStrain);
    Assert.Equal(0, wheel.Bursts);
  }

  #endregion

  #region Spans

  private static bool[] Cells(string pattern) =>
    [.. pattern.Select(c => c == 'S')];

  // Fails when a gap of exactly the span is refused, or a longer one passes.
  [Fact]
  public void Span_GapBeyondSpanBreaches() {
    Assert.Null(MpShaftSpans.Breach(Cells("S...S"), 4, 1));

    var breach = MpShaftSpans.Breach(Cells("S....S"), 4, 1);

    Assert.Equal(new MpSpanBreach(0, 5, 4, false), breach);
  }

  // Fails when either free end is not checked, or an overhang of exactly the limit is refused.
  [Fact]
  public void Span_OverhangBeyondLimitBreaches() {
    Assert.Null(MpShaftSpans.Breach(Cells(".S...S."), 4, 1));

    Assert.Equal(
      new MpSpanBreach(0, 2, 1, true),
      MpShaftSpans.Breach(Cells("..S"), 4, 1)
    );
    Assert.Equal(
      new MpSpanBreach(1, 2, 1, true),
      MpShaftSpans.Breach(Cells(".S.."), 4, 1)
    );
    Assert.Equal(
      new MpSpanBreach(0, 3, 1, true),
      MpShaftSpans.Breach(Cells("..."), 4, 1)
    );
  }

  #endregion
}
