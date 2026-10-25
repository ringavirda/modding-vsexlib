using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The overrunning coupling of an "mpshaft" run, locking, overrunning, thrown out and slipping: <see cref="BEBehaviorMpOverrun"/> and <see cref="MpShaftNetwork"/>.</summary>
public class MpOverrunTests {
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

  private static readonly BlockPos Engine = new(0, 0, 4);

  private static int _ids = 4000;

  private static void Put(
    TestWorld w,
    BlockPos pos,
    BlockEntity be,
    BEBehaviorNetworkMember member
  ) {
    be.Behaviors.Add(member);
    int id = _ids++;
    w.Place(
      pos,
      TestBlocks.Configure(new Block(), $"test:overrun{id}", id),
      be
    );
    w.Initialize(be);
  }

  /// <summary>A driver cell at A and a driven cell at D, joined through the host at F, which holds a coupling of <paramref name="sense"/> when <paramref name="coupled"/>.</summary>
  private static (
    TestWorld World,
    Cell Driver,
    Cell Driven,
    BEBehaviorMpOverrun? Coupling
  ) Line(int sense = 1, bool coupled = true) {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    var a = new Cell(100f);
    var d = new Cell(100f);
    var host = new Host();
    Put(w, A, a, new CellMember(a, "es"));
    Put(w, F, host, new LinkMember(host));
    Put(w, D, d, new CellMember(d, "wn"));
    BEBehaviorMpOverrun? coupling = null;
    if (coupled) {
      coupling = new BEBehaviorMpOverrun(host);
      coupling.Connect(NodeKey.Port(F, 0), NodeKey.Port(F, 1), sense);
      host.Behaviors.Add(coupling);
    }
    return (w, a, d, coupling);
  }

  private static MpShaftNetwork Run(TestWorld w) =>
    (MpShaftNetwork)(w.NetworkAt(A) ?? w.NetworkAt(Engine))!;

  private static void Tick(TestWorld w, int steps = 1) {
    for (int i = 0; i < steps; i++)
      Run(w).OnTick(w.Accessor, 0.2f, w.Networks);
  }

  private static float SpeedAt(TestWorld w, BlockPos pos) {
    var store = Run(w).Store;
    return store.NodeSpeed(store.IndexOf(NodeKey.Centre(pos)));
  }

  // Fails when a coupling that has overrun never catches again: the driver outruns the driven run instead of turning with it.
  [Fact]
  public void Overrun_locks_when_driver_catches_up() {
    var (w, driver, driven, _) = Line();
    driven.Drive = 300f;
    Tick(w, 50);
    float drivenAlone = SpeedAt(w, D);
    float driverAlone = SpeedAt(w, A);

    driver.Drive = 2000f;
    Tick(w, 40);

    Assert.True(drivenAlone > 5f);
    Assert.True(driverAlone < drivenAlone / 2f);
    Assert.True(SpeedAt(w, A) > drivenAlone);
    Assert.Equal(SpeedAt(w, D), SpeedAt(w, A), 0.5f);
  }

  // Fails when the coupling locks while the driven run is ahead: the wheel run then turns with the engine run instead of by its own drive.
  [Fact]
  public void Overrun_carries_nothing_when_driven_ahead() {
    var (w, wheel, engine, _) = Line();
    wheel.Drive = 100f;
    engine.Drive = 600f;
    var (alone, aloneWheel, _, _) = Line(coupled: false);
    aloneWheel.Drive = 100f;

    Tick(w, 50);
    Tick(alone, 50);

    Assert.True(SpeedAt(w, D) > SpeedAt(w, A));
    Assert.Equal(SpeedAt(alone, A), SpeedAt(w, A), 0.01f);
  }

  // Fails when the thrown flag is ignored in either sense: the driven run then follows the driver.
  [Fact]
  public void Thrown_out_carries_nothing() {
    var (w, driver, _, coupling) = Line();
    coupling!.Thrown = true;
    driver.Drive = 1000f;
    var (mirrored, mirroredCoupling, mirroredDriver, mirroredDriven) = Reversed(
      -1
    );
    mirroredCoupling.Thrown = true;

    Tick(w, 30);
    Tick(mirrored, 30);

    Assert.True(SpeedAt(w, A) > 5f);
    Assert.Equal(0f, SpeedAt(w, D), 1e-3f);
    Assert.True(SpeedAt(mirrored, mirroredDriver) < -5f);
    Assert.Equal(0f, SpeedAt(mirrored, mirroredDriven), 1e-3f);
  }

  // Fails when the slip carries other than 0.8 of the 17.0 kN*m cap: the free 100 kg*m^2 side turns by torque * 0.2 s / 100 in the step.
  [Fact]
  public void Slips_at_point_eight_of_cap() {
    var (w, driver, _, _) = Line();
    driver.Drive = 40000f;

    Tick(w);

    float carriedKNm = SpeedAt(w, D) * 100f / 0.2f / 1000f;
    Assert.Equal(13.6f, carriedKNm, 0.1f);
  }

  private sealed class Frame : BlockEntity, IMpEnergyLink {
    public NodeKey From => NodeKey.Port(Pos, 0);

    public NodeKey To => NodeKey.Port(Pos, 1);

    public float Ratio => 1f;

    public int Sign => -1;

    public float CapNm => 1e9f;

    public float Efficiency => 1f;

    public bool Engaged => true;
  }

  private sealed class EastWestLink : BEBehaviorNetworkMember, INetworkMember {
    public EastWestLink(BlockEntity be)
      : base(be) {
      NetworkType = "mpshaft";
      DeclareConnectors("we");
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
        key.Slot == NodeKey.PortBase ? BlockFacing.WEST : BlockFacing.EAST
      );
    }
  }

  // An engine cell, a reversing frame, the driver cell turning against the engine, a coupling of the given sense, and the driven cell.
  private static (
    TestWorld World,
    BEBehaviorMpOverrun Coupling,
    BlockPos Driver,
    BlockPos Driven
  ) Reversed(int sense) {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    var engine = new Cell(100f) { Drive = 1000f };
    var frame = new Frame();
    var driver = new Cell(100f);
    var host = new Host();
    var driven = new Cell(100f);
    Put(w, Engine, engine, new CellMember(engine, "e"));
    Put(w, new BlockPos(1, 0, 4), frame, new EastWestLink(frame));
    Put(w, new BlockPos(2, 0, 4), driver, new CellMember(driver, "we"));
    Put(w, new BlockPos(3, 0, 4), host, new EastWestLink(host));
    Put(w, new BlockPos(4, 0, 4), driven, new CellMember(driven, "w"));
    var coupling = new BEBehaviorMpOverrun(host);
    coupling.Connect(
      NodeKey.Port(new BlockPos(3, 0, 4), 0),
      NodeKey.Port(new BlockPos(3, 0, 4), 1),
      sense
    );
    host.Behaviors.Add(coupling);
    return (w, coupling, new BlockPos(2, 0, 4), new BlockPos(4, 0, 4));
  }

  // Fails when the mirrored coupling's sign is dropped: it then overruns under the driver turning in its sense.
  [Fact]
  public void Reverse_sense_mirrored() {
    var (mirrored, _, _, mirroredDriven) = Reversed(-1);
    var (plain, _, _, plainDriven) = Reversed(1);

    Tick(mirrored, 30);
    Tick(plain, 30);

    Assert.True(SpeedAt(mirrored, mirroredDriven) < -5f);
    Assert.Equal(0f, SpeedAt(plain, plainDriven), 1e-3f);
  }
}
