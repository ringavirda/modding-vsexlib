using ExpandedLib.Networks;
using ExpandedLib.Testing;
using WidgetNamespace.BlockEntities;
using WidgetNamespace.Blocks;
using Vintagestory.API.MathTools;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>Three widget nodes in a straight line join one network.</summary>
public class WidgetTests {
  [Fact]
  public void Three_nodes_in_a_line_join_one_network() {
    var scene = new Scene().Network(
      "widgetnet",
      sys => new StubNetwork(sys, "widgetnet")
    );
    int id = 1;
    var diagram = new SceneDiagram().On(
      'w',
      p => scene.Node(p, Widget(scene, id++), new BlockEntityWidget(), "widgetnet")
    );
    diagram.Layer("www");
    scene.Build();

    StubNetwork? net0 = scene.NetworkAt<StubNetwork>(new BlockPos(0, 0, 0));
    StubNetwork? net1 = scene.NetworkAt<StubNetwork>(new BlockPos(1, 0, 0));
    StubNetwork? net2 = scene.NetworkAt<StubNetwork>(new BlockPos(2, 0, 0));

    Assert.NotNull(net0);
    Assert.Same(net0, net1);
    Assert.Same(net0, net2);
  }

  // OnLoaded resolves Type/Orientation off the variant map (TestBlocks.Configure only primes the
  // map itself); the real placement pipeline runs it at chunk load, so a scene fixture must too.
  private static BlockWidget Widget(Scene scene, int id) {
    var block = TestBlocks.Configure(
      new BlockWidget(),
      "widgetdomain:widget-we",
      id,
      ("orientation", "we")
    );
    block.OnLoaded(scene.World.Api);
    return block;
  }
}
