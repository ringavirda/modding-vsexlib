using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="PipeNetworkState.SettledPressure"/> through the network tick, produce and consume,
/// restore, merge and split. The runs are east-west lines of stand-in nodes whose two end connectors
/// face air, so every tick leaks gas from them.
/// </summary>
public class PipeSettledPressureTests {
  #region Fixtures

  private static readonly BlockPos West = new(0, 0, 0);
  private static readonly BlockPos Middle = new(1, 0, 0);
  private static readonly BlockPos East = new(2, 0, 0);

  private static TestWorld NewWorld(params BlockPos[] nodes) {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    foreach (BlockPos pos in nodes)
      Add(w, pos);
    return w;
  }

  private static void Add(TestWorld w, BlockPos pos, bool openUp = false) {
    w.Place(pos, LineNode.Of(openUp));
    w.AddNode(pos, "pipe");
  }

  private static PipeNetwork Run(TestWorld w, BlockPos pos) =>
    Assert.IsType<PipeNetwork>(w.NetworkAt(pos));

  private static void Charge(TestWorld w, BlockPos pos, float litres) =>
    Run(w, pos).TryProduceGas(litres, 20f, "Steam", w.Accessor);

  #endregion

  #region Network tick

  // Fails when the network tick does not set the settled pressure.
  [Fact]
  public void The_network_tick_settles_the_runs_pressure() {
    var w = NewWorld(West, Middle, East);
    Charge(w, West, 60f);

    Assert.Equal(0f, Run(w, West).State!.SettledPressure);
    w.Tick();

    PipeNetworkState state = Run(w, West).State!;
    Assert.InRange(state.Volume, 1f, 59f);
    Assert.Equal(
      state.Volume / (3 * ExlibValues.LitresPerPipe),
      state.SettledPressure,
      4
    );
  }

  // Fails when the settled pressure follows a produce or consume call between ticks.
  [Fact]
  public void Producing_and_consuming_between_ticks_leave_the_settled_pressure() {
    var w = NewWorld(West, Middle, East);
    Charge(w, West, 60f);
    w.Tick();
    PipeNetwork run = Run(w, West);
    float settled = run.State!.SettledPressure;

    Charge(w, West, 15f);
    run.TryConsumeGas(40f, w.Accessor);

    Assert.Equal(settled, run.State!.SettledPressure);
    Assert.NotEqual(settled, run.State!.Pressure, 3);
  }

  // Fails when the settled pressure is taken before the tick's leak loss.
  [Fact]
  public void The_settled_pressure_is_taken_after_the_leaks() {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    Add(w, West);
    Add(w, Middle, openUp: true);
    Add(w, East);
    PipeNetwork run = Run(w, West);
    run.RestoreState(
      new PipeNetworkState {
        Volume = 3 * ExlibValues.LitresPerPipe,
        MediumType = "Steam",
      }
    );

    w.Tick();

    Assert.Equal(
      run.State!.Volume / (3 * ExlibValues.LitresPerPipe),
      run.State!.SettledPressure,
      4
    );
    Assert.True(run.State!.SettledPressure < 1f);
  }

  #endregion

  #region Restore, merge, split

  // Fails when a restored run does not start settled at its saved pressure.
  [Fact]
  public void A_restored_run_starts_settled_at_its_pressure() {
    var w = NewWorld(West);
    PipeNetwork run = Run(w, West);

    run.RestoreState(
      new PipeNetworkState {
        Volume = 40f,
        MaxVolume = ExlibValues.LitresPerPipe,
        MediumType = "Steam",
        Pressure = 1.7f,
      }
    );

    Assert.Equal(1.7f, run.State!.SettledPressure);
  }

  // Fails when two gas runs merged keep either run's settled pressure.
  [Fact]
  public void Two_gas_runs_merged_settle_at_their_combined_pressure() {
    var w = NewWorld(West, East);
    Charge(w, West, 20f);
    Charge(w, East, 10f);

    Add(w, Middle);

    PipeNetwork run = Run(w, Middle);
    Assert.Equal(
      30f / (3 * ExlibValues.LitresPerPipe),
      run.State!.SettledPressure,
      4
    );
  }

  // Fails when a run that wins a merge over an incompatible medium keeps its settled pressure.
  [Fact]
  public void A_run_that_wins_a_merge_over_water_settles_at_its_new_pressure() {
    var w = NewWorld(West, East);
    Charge(w, West, 20f);
    Run(w, East).TryProduceLiquid(5f, 20f, 1f, w.Accessor);

    Add(w, Middle);

    PipeNetwork run = Run(w, Middle);
    Assert.Equal("Steam", run.State!.MediumType);
    Assert.Equal(
      20f / (3 * ExlibValues.LitresPerPipe),
      run.State!.SettledPressure,
      4
    );
  }

  // Fails when a split fragment does not start settled at its share's pressure.
  [Fact]
  public void A_split_fragment_starts_settled_at_its_pressure() {
    var w = NewWorld(West, Middle, East);
    Charge(w, West, 60f);
    w.Tick();
    Run(w, West).TryConsumeGas(30f, w.Accessor);

    float before = Run(w, West).State!.SettledPressure;

    w.RemoveNode(Middle);

    PipeNetworkState fragment = Run(w, West).State!;
    Assert.NotEqual(before, fragment.Pressure, 3);
    Assert.Equal(fragment.Pressure, fragment.SettledPressure, 4);
  }

  #endregion

  #region Stand-in blocks

  /// <summary>A pipe node with connectors east and west, and up when <see cref="OpenUp"/>; a
  /// connector with no node beyond it faces air.</summary>
  private sealed class LineNode : BlockNetworkNode {
    public bool OpenUp { get; init; }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.EAST
      || face == BlockFacing.WEST
      || (OpenUp && face == BlockFacing.UP);

    public static LineNode Of(bool openUp) =>
      TestBlocks.Configure(
        new LineNode { OpenUp = openUp },
        $"test:linenode-{(openUp ? "open" : "shut")}",
        openUp ? 2 : 1
      );
  }

  #endregion
}
