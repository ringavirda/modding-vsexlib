using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="IPipeVentSource"/>: a node's block supplies the strategy its open faces vent
/// through, the factory's strategy serves every other node, and a run with neither vents nothing.
/// Each run is a chimney-ventable node open on top under a chimney, holding steam below 1 atm.
/// </summary>
public class PipeVentSourceTests {
  #region Fixtures

  private const float FactoryRate = 16f;
  private const float NodeRate = 8f;
  private const float StartVolume = 24f;

  private static readonly BlockPos FactoryRunPos = new(0, 0, 0);
  private static readonly BlockPos NodeRunPos = new(10, 0, 0);

  private static readonly Block Chimney = TestBlocks.Configure(
    new Block(),
    "game:chimney",
    50
  );

  private static TestWorld NewWorld(float? factoryRate) {
    var w = new TestWorld();
    w.RegisterNetwork(
      "pipe",
      sys => new PipeNetwork(
        sys,
        factoryRate is float rate ? new ChimneyVent(() => rate) : null
      )
    );
    return w;
  }

  private static void PlaceVented(TestWorld w, BlockPos pos, Block node) {
    w.Place(pos.UpCopy(), Chimney);
    w.Place(pos, node);
    w.AddNode(pos, "pipe");
  }

  private static PipeNetwork Fill(TestWorld w, BlockPos pos, float volume) {
    var net = Assert.IsType<PipeNetwork>(w.NetworkAt(pos));
    net.RestoreState(
      new PipeNetworkState { Volume = volume, MediumType = "Steam" }
    );
    return net;
  }

  #endregion

  #region Vent strategies

  // Mutation: PipeNetwork.VentFor returns the factory's strategy for every node.
  [Fact]
  public void Two_runs_vent_each_at_its_own_strategys_rate() {
    var w = NewWorld(FactoryRate);
    PlaceVented(w, FactoryRunPos, VentNode.Plain(1));
    PlaceVented(w, NodeRunPos, VentNode.Sourcing(2, NodeRate));
    PipeNetwork factoryRun = Fill(w, FactoryRunPos, StartVolume);
    PipeNetwork nodeRun = Fill(w, NodeRunPos, StartVolume);

    w.Tick();

    Assert.Equal(StartVolume - FactoryRate, factoryRun.State!.Volume, 3);
    Assert.Equal(StartVolume - NodeRate, nodeRun.State!.Volume, 3);
  }

  // Mutation: ClassifyOpenings drops its air check, so the chimney face counts as a leak.
  [Fact]
  public void A_run_with_neither_strategy_vents_nothing() {
    var w = NewWorld(factoryRate: null);
    PlaceVented(w, FactoryRunPos, VentNode.Plain(1));
    PipeNetwork run = Fill(w, FactoryRunPos, StartVolume);

    w.Tick();

    Assert.Equal(StartVolume, run.State!.Volume, 3);
    Assert.Equal(0, run.State.OpeningsCount);
  }

  // Mutation: ApplyVentDraw stops after the first strategy.
  [Fact]
  public void A_run_mixing_both_vents_each_node_through_its_own_strategy() {
    var w = NewWorld(FactoryRate);
    BlockPos sourcingPos = FactoryRunPos.EastCopy();
    PlaceVented(w, FactoryRunPos, VentNode.Plain(1, BlockFacing.EAST));
    PlaceVented(
      w,
      sourcingPos,
      VentNode.Sourcing(2, NodeRate, BlockFacing.WEST)
    );
    PipeNetwork run = Fill(w, FactoryRunPos, 2 * StartVolume);
    Assert.Same(run, w.NetworkAt(sourcingPos));

    w.Tick();

    Assert.Equal(
      2 * StartVolume - FactoryRate - NodeRate,
      run.State!.Volume,
      3
    );
  }

  // Mutation: PipeNetwork.VentFor returns the node's null instead of falling back to the factory's.
  [Fact]
  public void A_block_supplying_no_strategy_vents_through_the_factorys() {
    var w = NewWorld(FactoryRate);
    PlaceVented(w, FactoryRunPos, VentNode.Sourcing(1, rate: null));
    PipeNetwork run = Fill(w, FactoryRunPos, StartVolume);

    w.Tick();

    Assert.Equal(StartVolume - FactoryRate, run.State!.Volume, 3);
  }

  // Mutation: PipeNetwork.VentFor calls CreateVentStrategy on every tick instead of keeping it.
  [Fact]
  public void Each_run_creates_its_nodes_strategy_once_and_keeps_it() {
    var w = NewWorld(factoryRate: null);
    SourcingVentNode sourcing = VentNode.Sourcing(1, NodeRate);
    PlaceVented(w, FactoryRunPos, sourcing);
    PlaceVented(w, NodeRunPos, sourcing);
    PipeNetwork first = Fill(w, FactoryRunPos, StartVolume);
    PipeNetwork second = Fill(w, NodeRunPos, StartVolume);

    w.Tick(2);

    Assert.Equal(2, sourcing.Created);
    Assert.Equal(StartVolume - 2 * NodeRate, first.State!.Volume, 3);
    Assert.Equal(StartVolume - 2 * NodeRate, second.State!.Volume, 3);
  }

  #endregion

  #region Stand-in blocks

  /// <summary>A chimney-ventable pipe node open on top, with one more connector when
  /// <see cref="Side"/> is set.</summary>
  private class VentNode : BlockNetworkNode, IChimneyVentable {
    public BlockFacing? Side { get; init; }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.UP || face == Side;

    public static VentNode Plain(int id, BlockFacing? side = null) =>
      TestBlocks.Configure(
        new VentNode { Side = side },
        $"test:ventnode-{id}",
        id
      );

    public static SourcingVentNode Sourcing(
      int id,
      float? rate,
      BlockFacing? side = null
    ) =>
      TestBlocks.Configure(
        new SourcingVentNode { Rate = rate, Side = side },
        $"test:sourcingventnode-{id}",
        id
      );
  }

  /// <summary>A <see cref="VentNode"/> whose block supplies a <see cref="ChimneyVent"/> at
  /// <see cref="Rate"/> L/s, or no strategy when it is null.</summary>
  private sealed class SourcingVentNode : VentNode, IPipeVentSource {
    public float? Rate { get; init; }

    public int Created { get; private set; }

    public IPipeVentStrategy? CreateVentStrategy() {
      Created++;
      return Rate is float rate ? new ChimneyVent(() => rate) : null;
    }
  }

  #endregion
}
