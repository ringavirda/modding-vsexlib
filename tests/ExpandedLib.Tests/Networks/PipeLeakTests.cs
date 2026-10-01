using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers leak loss: gas per open end at 1 atm in proportion to pressure, water per open end, both
/// per second of the tick. Each run is one node whose connectors all face air.
/// </summary>
public class PipeLeakTests {
  #region Fixtures

  private static readonly BlockPos RunPos = new(0, 0, 0);

  private static TestWorld NewWorld(int openEnds) {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    w.Place(RunPos, OpenNode.With(openEnds));
    w.AddNode(RunPos, "pipe");
    return w;
  }

  private static PipeNetwork Fill(TestWorld w, float volume, string medium) {
    var net = Assert.IsType<PipeNetwork>(w.NetworkAt(RunPos));
    net.RestoreState(
      new PipeNetworkState { Volume = volume, MediumType = medium }
    );
    return net;
  }

  #endregion

  #region Gas

  // Fails when the gas loss ignores the open-end count.
  [Fact]
  public void Two_open_ends_lose_twice_the_gas_of_one() {
    var one = NewWorld(openEnds: 1);
    var two = NewWorld(openEnds: 2);
    PipeNetwork oneRun = Fill(one, ExlibValues.LitresPerPipe, "Steam");
    PipeNetwork twoRun = Fill(two, ExlibValues.LitresPerPipe, "Steam");

    one.Tick();
    two.Tick();

    Assert.Equal(
      ExlibValues.LitresPerPipe - ExlibValues.GasLeakRate,
      oneRun.State!.Volume,
      3
    );
    Assert.Equal(
      ExlibValues.LitresPerPipe - 2f * ExlibValues.GasLeakRate,
      twoRun.State!.Volume,
      3
    );
  }

  // Fails when the gas loss ignores the run's pressure.
  [Fact]
  public void A_run_at_half_an_atmosphere_loses_half_the_gas() {
    var w = NewWorld(openEnds: 1);
    float half = ExlibValues.LitresPerPipe / 2f;
    PipeNetwork run = Fill(w, half, "Steam");

    w.Tick();

    Assert.Equal(half - ExlibValues.GasLeakRate / 2f, run.State!.Volume, 3);
  }

  // Fails when the gas loss ignores the tick's dt.
  [Fact]
  public void A_two_second_tick_loses_twice_the_gas_of_a_one_second_tick() {
    var w = NewWorld(openEnds: 1);
    float charged = 3f * ExlibValues.LitresPerPipe;
    PipeNetwork run = Fill(w, charged, "Steam");

    w.Networks.ServerTick(w.Accessor, 2f);

    Assert.Equal(
      charged - 2f * 3f * ExlibValues.GasLeakRate,
      run.State!.Volume,
      3
    );
  }

  #endregion

  #region Water

  // Fails when the water loss ignores the open-end count.
  [Fact]
  public void Two_open_ends_drain_twice_the_water_of_one() {
    var one = NewWorld(openEnds: 1);
    var two = NewWorld(openEnds: 2);
    PipeNetwork oneRun = Fill(one, ExlibValues.LitresPerPipe, "Water");
    PipeNetwork twoRun = Fill(two, ExlibValues.LitresPerPipe, "Water");

    one.Tick();
    two.Tick();

    Assert.Equal(
      ExlibValues.LitresPerPipe - ExlibValues.LiquidLeakRate,
      oneRun.State!.Volume,
      3
    );
    Assert.Equal(
      ExlibValues.LitresPerPipe - 2f * ExlibValues.LiquidLeakRate,
      twoRun.State!.Volume,
      3
    );
  }

  #endregion

  #region Stand-in blocks

  /// <summary>A pipe node whose connectors are its first <see cref="OpenEnds"/> faces of up,
  /// north and east, every one facing air.</summary>
  private sealed class OpenNode : BlockNetworkNode {
    private static readonly BlockFacing[] Faces =
    [
      BlockFacing.UP,
      BlockFacing.NORTH,
      BlockFacing.EAST,
    ];

    public int OpenEnds { get; init; }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      System.Array.IndexOf(Faces, face) is int i && i >= 0 && i < OpenEnds;

    public static OpenNode With(int openEnds) =>
      TestBlocks.Configure(
        new OpenNode { OpenEnds = openEnds },
        $"test:opennode-{openEnds}",
        openEnds
      );
  }

  #endregion
}
