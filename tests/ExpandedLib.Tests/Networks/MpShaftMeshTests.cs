using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Ratings across a face and the mesh strain of a segment: <see cref="IMpEnergySegment.RatingAcross"/>,
/// <see cref="MpShaftStore"/> joint ends and <see cref="IMpEnergyMeshes"/> on <see cref="MpShaftNetwork"/>.
/// </summary>
public class MpShaftMeshTests {
  private sealed class PlainSegment : BlockEntity, IMpEnergySegment {
    public MpShaftSize Size => MpShaftSize.Main;

    public float RatingNm => 100f;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }
  }

  private sealed class Drive
    : BlockEntity,
      IMpEnergySegment,
      IMpEnergyStorage,
      IMpEnergyProducer {
    public float Torque { get; set; }

    public float Inertia => 1f;

    public MpShaftSize Size => MpShaftSize.Main;

    public float RatingNm => 1000f;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }

    public float DriveTorque(float speed) => Torque;
  }

  private sealed class Load
    : BlockEntity,
      IMpEnergySegment,
      IMpEnergyStorage,
      IMpEnergyConsumer {
    public float Torque { get; set; }

    public float Inertia => 1f;

    public MpShaftSize Size => MpShaftSize.Main;

    public float RatingNm => 1000f;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }

    public float LoadTorque(float speed) => Torque;
  }

  /// <summary>A junction rated 100 N*m on its own axis and 40 across its geared face.</summary>
  private sealed class Junction
    : BlockEntity,
      IMpEnergySegment,
      IMpEnergyStorage,
      IMpEnergyMeshes {
    private readonly Dictionary<BlockFacing, float> _strain = [];

    public float GearedRating { get; set; } = 40f;

    public float GearedUltimate { get; set; }

    public List<BlockFacing> Stripped { get; } = [];

    public float Inertia => 1f;

    public MpShaftSize Size => MpShaftSize.Main;

    public float RatingNm => 100f;

    public float RatingAcross(BlockFacing face) =>
      face == BlockFacing.UP ? GearedRating : RatingNm;

    public float StandingNm => 0f;

    public float StiffnessNmPerRad => 1f;

    public float Strain { get; set; }

    public float UltimateNm => 0f;

    public IReadOnlyCollection<BlockFacing> MeshFaces => [BlockFacing.UP];

    public float MeshStrain(BlockFacing face) =>
      _strain.GetValueOrDefault(face);

    public void SetMeshStrain(BlockFacing face, float strain) =>
      _strain[face] = strain;

    public void Strip(BlockFacing face) => Stripped.Add(face);
  }

  private static void Tick(TestWorld w, MpShaftNetwork net, double hours) {
    w.AdvanceHours(hours);
    net.OnTick(w.Accessor, 0.2f, w.Networks);
  }

  /// <summary>A drive, the junction and a load in a row, joined by hand: the junction's end of the load's joint crosses the geared face. A <paramref name="through"/> above 0 adds a second load past the junction on its own axis, so the drive carries both.</summary>
  private static (
    TestWorld World,
    MpShaftNetwork Net,
    Drive Drive,
    Junction Junction,
    Load Load
  ) Row(float torque, float through = 0f) {
    var w = new TestWorld();
    var drive = new Drive { Torque = torque + through };
    var junction = new Junction();
    var load = new Load { Torque = torque };
    var net = new MpShaftNetwork(w.Networks);
    int id = 300;
    var parts = new List<(int, BlockEntity)>
    {
      (0, drive),
      (1, junction),
      (2, load),
    };
    if (through > 0f)
      parts.Add((3, new Load { Torque = through }));
    foreach (var (x, be) in parts) {
      var pos = new BlockPos(x, 0, 0);
      w.Place(
        pos,
        TestBlocks.Configure(new Block(), $"test:mpmesh{id++}", id),
        be
      );
      net.Nodes.Add(pos);
      net.NodeKeys.Add(NodeKey.Centre(pos));
    }
    Tick(w, net, 0);
    int at(int x) => net.Store.IndexOf(NodeKey.Centre(new BlockPos(x, 0, 0)));
    Assert.True(net.Store.TryAddEdge(at(0), at(1), 1f, 1));
    Assert.True(
      net.Store.TryAddEdge(
        at(1),
        at(2),
        1f,
        1,
        1f,
        BlockFacing.UP,
        null,
        junction.RatingAcross(BlockFacing.UP),
        -1f
      )
    );
    if (through > 0f)
      Assert.True(net.Store.TryAddEdge(at(1), at(3), 1f, 1));
    return (w, net, drive, junction, load);
  }

  // Fails when the default answers anything but the segment's own rating.
  [Fact]
  public void RatingAcross_DefaultsToTheSegmentsRating() {
    IMpEnergySegment segment = new PlainSegment();

    Assert.Equal(100f, segment.RatingAcross(BlockFacing.UP));
    Assert.Equal(100f, segment.RatingAcross(BlockFacing.NORTH));
  }

  // Fails when a joint's load reads the node's rating instead of the rating stored for its end.
  [Fact]
  public void EdgeLoad_ReadsTheRatingOfItsOwnEnd() {
    var (w, net, _, _, _) = Row(80f);
    Tick(w, net, 0.01);

    float across = net.Store.EdgeLoad(1);
    float main = net.Store.EdgeLoad(0);

    Assert.Equal(2f, across, 2);
    Assert.Equal(0.8f, main, 2);
  }

  // Fails when a joint with no stored rating stops reading its node's: every other segment keeps its behaviour.
  [Fact]
  public void EdgeLoad_WithoutAnEndRatingReadsTheNodes() {
    var store = new MpShaftStore();
    int g = store.AddGroup();
    int a = store.AddNode(NodeKey.Centre(new BlockPos(0, 0, 0)), g, 1f, 0f);
    int b = store.AddNode(NodeKey.Centre(new BlockPos(1, 0, 0)), g, 1f, 0f);
    store.SetRating(a, 50f);
    store.SetRating(b, 50f);
    store.TryAddEdge(a, b, 1f, 1);
    store.SetDrive(a, 100f);
    store.SetLoad(b, 100f);
    store.Step(0.2f, float.PositiveInfinity);
    store.SolveTorquePath();

    Assert.Equal(2f, store.EdgeLoad(0), 2);
    Assert.Equal(2f, store.NodeLoad(b), 2);
  }

  // Fails when a mesh face's load accrues to the segment's own strain.
  [Fact]
  public void MeshFace_StrainsApartFromTheCell() {
    var (w, net, _, junction, _) = Row(80f);

    Tick(w, net, 0.25);

    Assert.Equal(0.25f, junction.MeshStrain(BlockFacing.UP), 3);
    Assert.Equal(0f, junction.Strain);
  }

  // Fails when the strip waits for the cell's own strain or is not called at the face's strain 1.
  [Fact]
  public void MeshFace_StripsAtStrainOneAndTheCellStays() {
    var (w, net, _, junction, _) = Row(80f);

    Tick(w, net, 0.6);
    Assert.Empty(junction.Stripped);
    Tick(w, net, 0.6);

    Assert.Equal(1f, junction.MeshStrain(BlockFacing.UP));
    Assert.Equal(BlockFacing.UP, Assert.Single(junction.Stripped));
    Assert.Equal(0f, junction.Strain);
  }

  // Fails when a light face is held to the cell's rating of the through run or the through run to the face's.
  [Fact]
  public void MeshFace_UnderItsRatingStaysWhileTheMainCarriesMore() {
    var (w, net, _, junction, _) = Row(30f, 60f);

    Tick(w, net, 0.5);

    Assert.Equal(0f, junction.MeshStrain(BlockFacing.UP));
    Assert.Equal(0f, junction.Strain);
  }

  // Fails when the server switch off still strips a face.
  [Fact]
  public void MeshFace_FailureOffStripsNothing() {
    var (w, net, _, junction, _) = Row(80f);
    net.FailureOverride = false;

    Tick(w, net, 2);

    Assert.Equal(1f, junction.MeshStrain(BlockFacing.UP));
    Assert.Empty(junction.Stripped);
  }
}
