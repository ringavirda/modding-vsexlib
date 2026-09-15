using System;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace PlatedPipes.Tests;

/// <summary>
/// The plated tier as a network: three straight segments in a row join one pipe main, and the tier's
/// registered burst pressure and throughput read back the values <see cref="PlatedPipesModSystem"/>
/// registers from config.
/// </summary>
public class PlatedPipeNetworkTests : IDisposable {
  // BlockPipe's per-tier registries are static, so a plated registration here would otherwise leak
  // into whatever test runs next.
  public void Dispose() {
    BlockPipe.RegisterBurst(BlockPipe.PlatedTier, () => 2.5f);
    BlockPipe.RegisterThroughput(BlockPipe.PlatedTier, () => 50f);
    BlockPipe.RegisterJoint(BlockPipe.PlatedTier, BlockPipe.FlangedJoint);
  }

  private static BlockPipe Segment(string orientation, int id) {
    var block = TestBlocks.Configure(
      new BlockPipe(),
      $"platedpipes:pipe-plated-straight-{orientation}",
      id,
      ("type", "straight"),
      ("tier", BlockPipe.PlatedTier),
      ("orientation", orientation)
    );
    block.SetNetworkTypeForTest("pipe");
    block.ApplyOrientationForTest(orientation);
    return block;
  }

  [Fact]
  public void Three_straight_segments_in_a_row_form_one_pipe_network() {
    using var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    for (int i = 0; i < 3; i++) {
      var pos = new BlockPos(i, 0, 0);
      var be = new BlockEntityPipe();
      world.Place(pos, Segment("we", 100 + i), be);
      world.Attach(be);
      world.AddNode(pos, "pipe");
    }

    var net = world.NetworkAt(new BlockPos(0, 0, 0));
    Assert.NotNull(net);
    Assert.Equal(3, net!.Nodes.Count);
    Assert.Same(net, world.NetworkAt(new BlockPos(1, 0, 0)));
    Assert.Same(net, world.NetworkAt(new BlockPos(2, 0, 0)));
  }

  [Fact]
  public void The_plated_tier_reports_its_registered_burst_pressure_and_throughput() {
    BlockPipe.RegisterBurst(
      BlockPipe.PlatedTier,
      () => PlatedPipesValues.PlatedPipeBurstPressure
    );
    BlockPipe.RegisterThroughput(
      BlockPipe.PlatedTier,
      () => PlatedPipesValues.PlatedPipeThroughput
    );

    BlockPipe segment = Segment("we", 200);

    Assert.Equal(
      PlatedPipesValues.PlatedPipeBurstPressure,
      segment.BurstPressure
    );
    Assert.Equal(PlatedPipesValues.PlatedPipeThroughput, segment.MaxThroughput);
  }
}
