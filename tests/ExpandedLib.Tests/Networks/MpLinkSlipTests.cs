using System;
using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Friction links under speed, in rings, across seams and from saved or declared state:
/// <see cref="BEBehaviorMpBelt"/>, <see cref="BEBehaviorMpClutch"/> and <see cref="MpShaftNetwork"/>.
/// </summary>
public class MpLinkSlipTests {
  private sealed class Cell(float inertia)
    : BlockEntity,
      IMpEnergyStorage,
      IMpEnergyProducer {
    public float Drive { get; set; }

    public float Inertia => inertia;

    public float DriveTorque(float speed) => Drive;
  }

  private sealed class Host : BlockEntity;

  private sealed class FrameEntity : BlockEntity, IMpEnergyLink {
    public NodeKey From => NodeKey.Port(Pos, 0);

    public NodeKey To => NodeKey.Port(Pos, 1);

    public float Ratio { get; set; } = 1f;

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
  private static readonly BlockPos C = new(0, 0, 1);
  private static readonly BlockPos H = new(1, 0, 2);
  private static readonly BlockPos G = new(1, 0, 3);

  private static int _ids = 9000;

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
    w.Place(pos, TestBlocks.Configure(new Block(), $"test:slip{id}", id), be);
    w.Initialize(be);
  }

  private static MpShaftNetwork Run(TestWorld w) =>
    (MpShaftNetwork)w.NetworkAt(A)!;

  private static void Tick(TestWorld w) =>
    Run(w).OnTick(w.Accessor, 0.2f, w.Networks);

  private static float SpeedAt(TestWorld w, BlockPos pos) {
    var store = Run(w).Store;
    return store.NodeSpeed(store.IndexOf(NodeKey.Centre(pos)));
  }

  // Fails when a link over its cap at speed counts its first slip step as met and relocks, so it carries the whole demand.
  [Fact]
  public void Belt_OverCapAtSpeedKeepsSlipping() {
    var w = NewWorld();
    var a = new Cell(100f) { Drive = 600f };
    var d = new Cell(100f);
    var host = new Host();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, host, new LinkMember(host));
    Put(w, D, d, new CellMember(d, "wn"));
    var belt = new BEBehaviorMpBelt(host);
    belt.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), 1, 1);
    host.Behaviors.Add(belt);
    for (int i = 0; i < 200; i++)
      Tick(w);
    float a0 = SpeedAt(w, A);
    float d0 = SpeedAt(w, D);

    a.Drive = 1000f;
    for (int i = 0; i < 50; i++)
      Tick(w);
    float a1 = SpeedAt(w, A);
    float d1 = SpeedAt(w, D);

    Assert.True(
      a1 - d1 > 10f,
      $"before A {a0} D {d0}; after 10 s at 1000 N*m A {a1} D {d1}; a slipping belt gives D about {d0 + 31.2f}"
    );
  }

  // Fails when a belt engaged at rest into a mismatched ring is not kept slipping once the run turns.
  [Fact]
  public void Belt_InAMismatchedRingEngagedAtRestSlips() {
    var w = NewWorld();
    var a = new Cell(100f) { Drive = 1000f };
    var c = new Cell(100f);
    var d = new Cell(100f);
    var host = new Host();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, host, new LinkMember(host));
    Put(w, D, d, new CellMember(d, "wn"));
    Put(w, C, c, new CellMember(c, "ne"));
    var belt = new BEBehaviorMpBelt(host);
    belt.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), 1, 2);
    host.Behaviors.Add(belt);

    for (int i = 0; i < 20; i++)
      Tick(w);

    float wa = SpeedAt(w, A);
    Assert.True(
      wa < 12f,
      $"A {wa}; with no drag {1000f * 0.2f * 20 / 300f}; slipping at 312 N*m {(1000f - 312f) * 0.2f * 20 / 300f}"
    );
  }

  // Fails when a merge across a geared seam mixes the other run in at its old ratios instead of its own.
  [Fact]
  public void Merge_AcrossAGearedSeamConservesMomentum() {
    var w = NewWorld();
    var a = new Cell(1f);
    var f = new FrameEntity { Ratio = 2f };
    var d = new Cell(1f);
    var g = new Cell(100f);
    var h = new Cell(1f);
    Put(w, A, a, new CellMember(a, "e"));
    Put(w, F, f, new LinkMember(f));
    Put(w, D, d, new CellMember(d, "ns"));
    Tick(w);
    Run(w).Store.Mix(0, 1e6f, 10f);
    float w0 = SpeedAt(w, A);
    Put(w, G, g, new CellMember(g, "n"));
    ((MpShaftNetwork)w.NetworkAt(G)!).OnTick(w.Accessor, 0.2f, w.Networks);

    Put(w, H, h, new CellMember(h, "ns"));
    Tick(w);

    Assert.Equal(w0 * 1.25f / 26.5f, SpeedAt(w, A), 2);
  }

  // Fails when a slipping geared or crossed belt takes the slip torque off the input and puts it on the output without the ratio or the crossed sign.
  [Theory]
  [InlineData(1, 2, false, 5.376f, 1.248f)]
  [InlineData(1, 2, true, 5.376f, -1.248f)]
  [InlineData(2, 1, false, 4.752f, 0.624f)]
  public void Belt_GearedAndCrossedSlipTorques(
    int from,
    int to,
    bool crossed,
    float expectA,
    float expectD
  ) {
    var w = NewWorld();
    var a = new Cell(100f) { Drive = 3000f };
    var d = new Cell(100f);
    var host = new Host();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, host, new LinkMember(host));
    Put(w, D, d, new CellMember(d, "wn"));
    var belt = new BEBehaviorMpBelt(host);
    belt.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), from, to, crossed);
    host.Behaviors.Add(belt);

    Tick(w);

    Assert.Equal(expectA, SpeedAt(w, A), 2);
    Assert.Equal(expectD, SpeedAt(w, D), 2);
  }

  private sealed class Anchor(float load)
    : BlockEntity,
      IMpEnergyStorage,
      IMpEnergyConsumer {
    public float Inertia => 100f;

    public float LoadTorque(float speed) => load;

    public float StandingNm => load;
  }

  // Fails when the cap takes the large pulley's grip, or ignores the pulley the grip is on: the grip of the smaller pulley, taken to the driving shaft.
  [Theory]
  [InlineData(1, 2, 390f)]
  [InlineData(2, 1, 780f)]
  [InlineData(2, 2, 1300f)]
  public void Belt_CapByPulleySize(int from, int to, float cap) =>
    Assert.Equal(cap, MpBeltWear.CapNm(from, to), 2);

  // Fails when the belt's life is taken from another torque or speed than the slip torque at the rated speed, or from another span than one in-game day.
  [Fact]
  public void Belt_WornOutAfterOneDayAtRatedSlip() {
    var w = NewWorld();
    var a = new Cell(1e6f) { Drive = 312f };
    var d = new Anchor(400f);
    var host = new Host();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, host, new LinkMember(host));
    Put(w, D, d, new CellMember(d, "wn"));
    var belt = new BEBehaviorMpBelt(host);
    belt.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), 1, 1);
    host.Behaviors.Add(belt);
    Run(w).FailureOverride = true;
    Run(w)
      .RestoreState(
        NodeKey.Centre(A),
        new MpShaftRunState(ExlibValues.MpBeltRatedRadS, 1e6f)
      );
    Tick(w);
    Tick(w);

    w.AdvanceHours(12);
    Tick(w);

    Assert.Equal(0.5f, belt.Wear, 0.02f);
    Assert.False(belt.Broken);

    w.AdvanceHours(12.5);
    Tick(w);

    Assert.True(belt.Broken);
  }

  // Fails when a clutch reloads released or its clamp restarts from nothing.
  [Fact]
  public void Clutch_SavesEngagementAndClamp() {
    var host = new Host();
    var clutch = new BEBehaviorMpClutch(host);
    clutch.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), MpShaftSize.Main);
    clutch.Engaged = true;
    clutch.Advance(1.5f, 0f, 0f);
    var tree = new TreeAttribute();
    clutch.ToTreeAttributes(tree);

    var loaded = new BEBehaviorMpClutch(new Host());
    loaded.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), MpShaftSize.Main);
    loaded.FromTreeAttributes(tree, null!);

    Assert.True(loaded.Engaged);
    Assert.Equal(clutch.Clamp, loaded.Clamp, 4);
    Assert.True(loaded.Clamp > 0f);
  }

  // Fails when a snapped belt reloads whole and snaps a second time.
  [Fact]
  public void Belt_SavesBroken() {
    var belt = new BEBehaviorMpBelt(new Host());
    belt.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), 1, 1);
    belt.Wear = 1f;
    belt.Break();
    var tree = new TreeAttribute();
    belt.ToTreeAttributes(tree);

    var loaded = new BEBehaviorMpBelt(new Host());
    loaded.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), 1, 1);
    loaded.FromTreeAttributes(tree, null!);

    Assert.True(loaded.Broken);
    Assert.False(loaded.Engaged);
  }

  // Fails when a declared clutch or belt is not fitted from its properties and its cell's ports.
  [Fact]
  public void Declared_LinksFitThemselvesFromProperties() {
    var w = NewWorld();
    var host = new Host();
    Put(w, F, host, new LinkMember(host));
    var clutch = new BEBehaviorMpClutch(host);
    var belt = new BEBehaviorMpBelt(host);

    clutch.Initialize(
      Substitute.For<ICoreAPI>(),
      new JsonObject(JToken.Parse("{\"size\":\"line\"}"))
    );
    belt.Initialize(
      Substitute.For<ICoreAPI>(),
      new JsonObject(
        JToken.Parse("{\"fromPulley\":1,\"toPulley\":2,\"crossed\":true}")
      )
    );

    clutch.Engaged = true;
    clutch.Advance(100f, 0f, 0f);
    Assert.Equal(NodeKey.Port(F, 0), clutch.From);
    Assert.Equal(NodeKey.Port(F, 1), clutch.To);
    Assert.Equal(BEBehaviorMpClutch.FullCapNm(MpShaftSize.Line), clutch.CapNm);
    Assert.Equal(NodeKey.Port(F, 1), belt.To);
    Assert.Equal(-1, belt.Sign);
    Assert.Equal(2f, belt.Ratio);
    Assert.True(belt.Engaged);
  }

  private sealed class ForeignLink : BlockEntity, IMpEnergyLink {
    public MpBeltWear Part { get; } = new();

    public NodeKey From => NodeKey.Port(Pos, 0);

    public NodeKey To => NodeKey.Port(Pos, 1);

    public float Ratio => 1f;

    public int Sign => 1;

    public float CapNm => MpBeltWear.CapNm(Part.FromSize, Part.ToSize);

    public float Efficiency => 1f;

    public bool Engaged => !Part.Broken;

    public float SlipTorque(float relativeSpeed) => Part.SlipTorque;

    public void Advance(float dt, float hours, float slipPowerW) =>
      Part.Advance(slipPowerW, hours);

    public bool WornOut => Part.WornOut;

    public void Break() => Part.Break();
  }

  private static (TestWorld World, ForeignLink Link) SlippingForeignLink() {
    var w = NewWorld();
    var a = new Cell(1e6f) { Drive = 312f };
    var d = new Anchor(400f);
    var link = new ForeignLink();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, link, new LinkMember(link));
    Put(w, D, d, new CellMember(d, "wn"));
    Run(w)
      .RestoreState(
        NodeKey.Centre(A),
        new MpShaftRunState(ExlibValues.MpBeltRatedRadS, 1e6f)
      );
    Tick(w);
    Tick(w);
    return (w, link);
  }

  // Fails when the part's Advance does not scale the slip power by the hours: half a day at the rated slip wears it by half.
  [Fact]
  public void Part_in_a_foreign_link_wears_as_the_belt() {
    var (w, link) = SlippingForeignLink();
    Run(w).FailureOverride = true;

    w.AdvanceHours(12);
    Tick(w);

    Assert.Equal(0.5f, link.Part.Wear, 0.02f);
    Assert.False(link.Part.Broken);
  }

  // Fails when a worn-out belt snaps with shaft failure off: it wears to 1 and keeps carrying.
  [Fact]
  public void Failure_off_wears_to_one_without_snapping() {
    var (w, link) = SlippingForeignLink();
    Run(w).FailureOverride = false;
    int snaps = 0;
    link.Part.Snapped += () => snaps++;

    w.AdvanceHours(30);
    Tick(w);

    Assert.Equal(1f, link.Part.Wear);
    Assert.True(link.Part.WornOut);
    Assert.False(link.Part.Broken);
    Assert.True(link.Engaged);
    Assert.Equal(0, snaps);
  }

  // Fails when the broken flag or the wear is not written, so a snapped belt reloads whole.
  [Fact]
  public void Part_saves_wear_and_broken() {
    var part = new MpBeltWear { Wear = 0.4f };
    part.Break();
    var tree = new TreeAttribute();
    part.Save(tree);

    var loaded = new MpBeltWear();
    loaded.Load(tree);

    Assert.Equal(0.4f, loaded.Wear, 4);
    Assert.True(loaded.Broken);
  }

  // Fails when a size 2 pulley reads as size 1, or a size other than 1 and 2 is taken.
  [Fact]
  public void Sizes_read_one_and_two() {
    var host = new Host();
    var belt = new BEBehaviorMpBelt(host);

    belt.Initialize(
      Substitute.For<ICoreAPI>(),
      new JsonObject(JToken.Parse("{\"fromPulley\":2,\"toPulley\":2}"))
    );

    Assert.Equal(1300f, belt.CapNm, 2);
    Assert.Equal(390f, MpBeltWear.CapNm(1, 2), 2);
    Assert.Equal(2f, MpBeltWear.PulleyDiameter(2));
    Assert.Throws<ArgumentException>(() => MpBeltWear.CapNm(3, 1));
    Assert.Throws<ArgumentException>(() => MpBeltWear.CapNm(1, 0));
  }
}
