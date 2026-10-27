using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A node hosted on a structure filler cell: <see cref="MpShaftNetwork"/> reads the numbers and sets the turning
/// parts of the principal's block entity.
/// </summary>
public class MpShaftFillerNodeTests {
  private sealed class Wheel : BlockEntity, IMpEnergyStorage, IMpEnergyRim {
    public float Inertia => 10f;

    public float RimLimitRadS { get; set; } = 10f;

    public float RimStrain { get; set; }

    public int Bursts { get; private set; }

    public void Burst() => Bursts++;
  }

  private static readonly BlockPos Principal = new(0, 0, 0);
  private static readonly BlockPos Hub = new(0, 1, 0);

  private static readonly BlockPos SecondHub = new(0, 1, 1);

  private static (TestWorld World, MpShaftNetwork Net, Wheel Wheel) Hosted(
    bool twoHubs = false
  ) {
    var w = new TestWorld();
    var wheel = new Wheel();
    w.Place(
      Principal,
      TestBlocks.Configure(new Block(), "test:mpfillerwheel", 301),
      wheel
    );
    wheel.Behaviors.Add(new BEBehaviorTurningParts(wheel));
    w.Initialize(wheel);
    w.Place(
      Hub,
      TestBlocks.Configure(new Block(), "test:mpfillerhub", 302),
      new BlockEntityStructureFiller { Principal = Principal }
    );
    var net = new MpShaftNetwork(w.Networks);
    net.Nodes.Add(Hub);
    net.NodeKeys.Add(NodeKey.Centre(Hub));
    if (twoHubs) {
      w.Place(
        SecondHub,
        TestBlocks.Configure(new Block(), "test:mpfillerhub2", 303),
        new BlockEntityStructureFiller { Principal = Principal }
      );
      net.Nodes.Add(SecondHub);
      net.NodeKeys.Add(NodeKey.Centre(SecondHub));
    }
    return (w, net, wheel);
  }

  // Fails when the numbers of a node on a filler are read from the filler's own block entity.
  [Fact]
  public void A_filler_hosted_node_reports_its_principals_inertia_to_the_run() {
    var (w, net, _) = Hosted();
    using (w) {
      net.OnTick(w.Accessor, 0.2f, w.Networks);

      Assert.Equal(
        10f,
        net.Store.InertiaOf(net.Store.IndexOf(NodeKey.Centre(Hub))),
        3
      );
    }
  }

  // Fails when the rim of a filler-hosted node is read from the filler instead of the principal.
  [Fact]
  public void A_filler_hosted_node_strains_and_bursts_its_principals_rim() {
    var (w, net, wheel) = Hosted();
    using (w) {
      net.Store.AddNode(
        NodeKey.Centre(Hub),
        net.Store.AddGroup(15f),
        wheel.Inertia,
        0f
      );
      net.OnTick(w.Accessor, 0.2f, w.Networks);
      w.AdvanceHours(1);
      net.OnTick(w.Accessor, 0.2f, w.Networks);
      w.AdvanceHours(1);
      net.OnTick(w.Accessor, 0.2f, w.Networks);

      Assert.Equal(1f, wheel.RimStrain);
      Assert.True(wheel.Bursts > 0);
    }
  }

  // Fails when the rim of a principal is accrued and burst once per node it hosts instead of once.
  [Fact]
  public void A_principal_hosting_two_nodes_strains_and_bursts_its_rim_once() {
    var (w1, one, wheel1) = Hosted();
    var (w2, two, wheel2) = Hosted(twoHubs: true);
    using (w1)
    using (w2) {
      foreach (var (w, net, wheel) in new[] { (w1, one, wheel1), (w2, two, wheel2) }) {
        foreach (var key in net.NodeKeys)
          net.Store.AddNode(key, net.Store.AddGroup(15f), wheel.Inertia, 0f);
        net.OnTick(w.Accessor, 0.2f, w.Networks);
        w.AdvanceHours(1);
        net.OnTick(w.Accessor, 0.2f, w.Networks);
        w.AdvanceHours(1);
        net.OnTick(w.Accessor, 0.2f, w.Networks);
      }

      Assert.True(wheel1.Bursts > 0);
      Assert.Equal(wheel1.RimStrain, wheel2.RimStrain, 4);
      Assert.Equal(wheel1.Bursts, wheel2.Bursts);
    }
  }

  // Fails when the run is named on the filler's turning parts instead of the principal's.
  [Fact]
  public void The_principals_turning_parts_take_the_run_of_the_node_its_filler_hosts() {
    var (w, net, wheel) = Hosted();
    using (w) {
      net.OnTick(w.Accessor, 0.2f, w.Networks);
      var tree = new TreeAttribute();
      wheel.ToTreeAttributes(tree);

      var run = ((ITreeAttribute)tree["turnRun"]).GetTreeAttribute("s0");
      Assert.Equal(Hub.Y, run.GetInt("y"));
    }
  }
}
