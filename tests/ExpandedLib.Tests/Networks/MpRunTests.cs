using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>What a machine reads of its "mpshaft" run through <see cref="MpRun"/>: signed speed and the torque the rest of the run can still deliver.</summary>
public class MpRunTests {
  private sealed class DriveNode : BlockEntity, IMpEnergyProducer {
    public float DriveTorque(float speed) => 1000f;
  }

  private sealed class FallingDrive
    : BlockEntity,
      IMpEnergyProducer,
      IMpEnergyStorage {
    public float Inertia => 10f;

    public float DriveTorque(float speed) => 1000f * (1f - speed / 10f);

    public float DriveSlope(float speed) => -100f;
  }

  private sealed class LoadNode : BlockEntity, IMpEnergyConsumer {
    public float Load { get; set; }

    public float Standing { get; set; }

    public float LoadTorque(float speed) => Load;

    public float StandingNm => Standing;
  }

  private static BlockPos Cell(int x) => new(x, 0, 0);

  private static NodeKey Key(int x) => NodeKey.Centre(Cell(x));

  private static MpShaftNetwork Wire(
    TestWorld world,
    params (int X, BlockEntity Be)[] nodes
  ) {
    var net = new MpShaftNetwork(world.Networks);
    int id = 300;
    foreach (var (x, be) in nodes) {
      world.Place(
        Cell(x),
        TestBlocks.Configure(new Block(), $"test:mprun{id}", id),
        be
      );
      id++;
      net.Nodes.Add(Cell(x));
      net.NodeKeys.Add(Key(x));
    }
    return net;
  }

  private static void Seed(MpShaftNetwork run, float speed) {
    int group = run.Store.AddGroup(speed);
    foreach (var key in run.NodeKeys.OrderBy(k => k.Pos.X))
      run.Store.AddNode(key, group, 10f, 0f);
  }

  // Fails when the available torque is read as the drive's whole torque, or leaves out the other machine's standing torque, or counts the reader's own load.
  [Fact]
  public void Read_DrivenRunIsTheDriveLessTheOthers() {
    var w = new TestWorld();
    var a = new LoadNode { Load = 300f, Standing = 50f };
    var net = Wire(
      w,
      (0, new DriveNode()),
      (1, a),
      (2, new LoadNode { Load = 120f })
    );
    Seed(net, 0f);

    net.OnTick(w.Accessor, 0.2f, w.Networks);

    Assert.Equal(1000f - 350f - 120f, net.Read(Key(0)).AvailableTorqueNm, 2);
    Assert.Equal(1000f - 120f, net.Read(Key(1)).AvailableTorqueNm, 2);
    Assert.Equal(1000f - 350f, net.Read(Key(2)).AvailableTorqueNm, 2);
  }

  // Fails when the drive is read at the run's present speed, which leaves a settled run offering only the reader's own draw.
  [Fact]
  public void Read_SettledRunOffersItsDriveHeadroom() {
    var w = new TestWorld();
    var net = Wire(
      w,
      (0, new FallingDrive()),
      (1, new LoadNode { Load = 100f }),
      (2, new LoadNode { Standing = 50f })
    );
    Seed(net, 0f);

    for (int i = 0; i < 300; i++)
      net.OnTick(w.Accessor, 0.2f, w.Networks);

    var reader = net.Read(Key(2));
    Assert.Equal(8.5f, reader.SpeedRadS, 2);
    Assert.Equal(1000f - 100f, reader.AvailableTorqueNm, 1);
  }

  // Fails when the speed is read unsigned.
  [Fact]
  public void Read_ReversedRunKeepsItsSign() {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    foreach (int x in new[] { 0, 1 })
      w.PlaceNode(Cell(x), "mpshaft", "we");
    var net = (MpShaftNetwork)w.NetworkAt(Cell(0))!;
    Seed(net, -3f);

    net.OnTick(w.Accessor, 0.2f, w.Networks);

    var reading = MpRun.Read(w.Networks, Key(1));
    Assert.Equal(-3f, reading.SpeedRadS, 2);
    Assert.Equal(0f, reading.AvailableTorqueNm);
  }

  // Fails when a node on no run, or a missing system, throws or reads anything but None.
  [Fact]
  public void Read_NodeOnNoRunIsNone() {
    var w = new TestWorld();
    w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    var net = Wire(w, (0, new DriveNode()));
    Seed(net, 2f);

    Assert.Equal(MpRunReading.None, MpRun.Read(w.Networks, Key(5)));
    Assert.Equal(MpRunReading.None, MpRun.Read(null, Key(0)));
    Assert.Equal(MpRunReading.None, net.Read(Key(5)));
  }
}
