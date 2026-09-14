using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using HandMill.BlockEntities;
using HandMill.Blocks;
using Vintagestory.API.MathTools;
using Xunit;

namespace HandMill.Tests;

/// <summary>A crank driving three shafts in a straight line: one network, four nodes, speed rises
/// while wound and falls back toward zero once the wind runs out.</summary>
public class ShaftLineTests {
  [Fact]
  public void A_wound_crank_spins_up_the_line_then_coasts_down() {
    var scene = new Scene().Network(
      "mpenergy",
      sys => new MpEnergyNetwork(sys)
    );
    int id = 1;
    var diagram = new SceneDiagram()
      .On(
        'c',
        p =>
          scene.Node(p, Crank(scene, id++), new BlockEntityCrank(), "mpenergy")
      )
      .On(
        '=',
        p =>
          scene.Node(p, Shaft(scene, id++), new BlockEntityShaft(), "mpenergy")
      );
    diagram.Layer("c===");
    scene.Build();

    var crankBe = scene.EntityAt<BlockEntityCrank>(new BlockPos(0, 0, 0))!;
    // Node() places and queues the graph add but does not run the real Initialize; the crank's own
    // unwind tick listener needs it, or the wind never depletes.
    scene.World.Initialize(crankBe);
    crankBe.Wind(10);
    // The first tick is deterministic from a cold start: full drive torque against only friction
    // saturates the run to its burst speed in one second.
    scene.Step(1);

    MpEnergyNetworkState? afterWind = scene
      .NetworkAt<MpEnergyNetwork>(new BlockPos(0, 0, 0))
      ?.State;
    Assert.NotNull(afterWind);
    Assert.True(afterWind!.Speed > 0f);

    // Past the 10-second wind and comfortably past the run's own coast-down: drive torque has been
    // zero for a while, so this reads a converged floor, not a snapshot mid-oscillation.
    scene.Step(30);

    MpEnergyNetworkState? afterCoast = scene
      .NetworkAt<MpEnergyNetwork>(new BlockPos(0, 0, 0))
      ?.State;
    Assert.NotNull(afterCoast);
    Assert.Equal(0f, afterCoast!.Speed);
  }

  // OnLoaded resolves Type/Orientation off the variant map (TestBlocks.Configure only primes the
  // map itself); the real placement pipeline runs it at chunk load, so a scene fixture must too.
  private static BlockCrank Crank(Scene scene, int id) {
    var block = TestBlocks.Configure(
      new BlockCrank(),
      "handmill:drive-crank-e",
      id,
      ("orientation", "e")
    );
    block.OnLoaded(scene.World.Api);
    return block;
  }

  private static BlockShaft Shaft(Scene scene, int id) {
    var block = TestBlocks.Configure(
      new BlockShaft(),
      "handmill:drive-shaft-we",
      id,
      ("orientation", "we")
    );
    block.OnLoaded(scene.World.Api);
    return block;
  }
}
