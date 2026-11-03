using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the gas throughflow of a run: <see cref="PipeNetwork.OfferedTotalLitres"/>,
/// <see cref="PipeNetwork.OfferCeiling"/> and <see cref="PipeNetwork.SettleThroughflow"/>. The runs are
/// east-west lines of three stand-in nodes, 90 L at 1 atm, shut at both ends by rock or left open to air.
/// </summary>
public class PipeThroughflowTests {
  #region Fixtures

  private static readonly BlockPos West = new(0, 0, 0);
  private static readonly BlockPos Middle = new(1, 0, 0);
  private static readonly BlockPos East = new(2, 0, 0);

  private static TestWorld NewRun(bool shut) {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    foreach (BlockPos pos in new[] { West, Middle, East }) {
      w.Place(pos, LineNode.Shut());
      w.AddNode(pos, "pipe");
    }
    if (shut) {
      Block rock = TestBlocks.Configure(new Block(), "game:rock-granite", 10);
      w.Place(West.WestCopy(), rock);
      w.Place(East.EastCopy(), rock);
    }
    return w;
  }

  private static PipeNetwork Run(TestWorld w) =>
    Assert.IsType<PipeNetwork>(w.NetworkAt(West));

  private static void Blow(TestWorld w, float litres, float ceiling) =>
    Run(w).TryProduceGas(
      litres,
      20f,
      "Air",
      w.Accessor,
      maxOutputPressure: ceiling
    );

  #endregion

  // Fails when the offer counts only the litres the pool had headroom for (94.5 L of the 200 offered).
  [Fact]
  public void An_offer_past_the_pools_headroom_counts_whole() {
    var w = NewRun(shut: true);

    Blow(w, 200f, 1.05f);

    Assert.Equal(1.05f, Run(w).State!.Pressure, 4);
    Assert.Equal(200.0, Run(w).OfferedTotalLitres, 3);
  }

  // Fails when an offer whose ceiling the leak clamp brings to 1 atm still counts.
  [Fact]
  public void A_leaking_run_counts_no_offer() {
    var w = NewRun(shut: false);
    Blow(w, 10f, 1.05f);
    w.Tick();
    double before = Run(w).OfferedTotalLitres;

    Blow(w, 200f, 1.05f);

    Assert.True(Run(w).State!.IsLeaking);
    Assert.Equal(before, Run(w).OfferedTotalLitres, 3);
  }

  // Fails when the ceiling is the last offer's, not the lowest, or still stands two ticks later.
  [Fact]
  public void The_offer_ceiling_is_the_lowest_and_lasts_one_tick_more() {
    var w = NewRun(shut: true);

    Blow(w, 10f, 1.05f);
    Blow(w, 10f, 1.2f);
    Assert.Equal(1.05f, Run(w).OfferCeiling, 4);

    w.Tick();
    Assert.Equal(1.05f, Run(w).OfferCeiling, 4);

    w.Tick();
    Assert.Equal(0f, Run(w).OfferCeiling);
  }

  // Fails when the settle leaves the pool where the blower left it or keeps the litres out of the flow readout.
  [Fact]
  public void Settling_writes_the_operating_point_and_counts_the_litres_as_flow() {
    var w = NewRun(shut: true);
    Blow(w, 200f, 1.05f);

    Run(w).SettleThroughflow(156f, 1.0065f, w.Accessor);
    w.Tick();

    PipeNetworkState state = Run(w).State!;
    Assert.Equal(1.0065f, state.Pressure, 4);
    Assert.Equal(1.0065f * 3 * ExlibValues.LitresPerPipe, state.Volume, 2);
    // The readout smooths one tick of 156 L/s at 0.3 from rest.
    Assert.Equal(0.3f * 156f, state.FlowRate, 1);
  }

  private sealed class LineNode : BlockNetworkNode {
    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.EAST || face == BlockFacing.WEST;

    public static LineNode Shut() =>
      TestBlocks.Configure(new LineNode(), "test:linenode-shut", 1);
  }
}
