using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using WidgetNamespace.Behaviors;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>The behaviour counts a tick and round-trips through the tree.</summary>
public class BEBehaviorWidgetTests {
  [Fact]
  public void Counts_a_tick_and_round_trips_through_the_tree() {
    var world = new TestWorld();
    var pos = new BlockPos(0, 0, 0);
    Block block = TestBlocks.Configure(new Block(), "widgetdomain:widget", 1);
    var be = new TestMemberBlockEntity();
    world.Place(pos, block, be);
    var behavior = new BEBehaviorWidget(be);
    be.Behaviors.Add(behavior);
    world.Initialize(be);

    world.FireBlockEntityTicks(times: 3);

    var tree = new TreeAttribute();
    behavior.ToTreeAttributes(tree);
    Assert.Equal(3, tree.GetInt("count"));

    var restored = new BEBehaviorWidget(be);
    restored.FromTreeAttributes(tree, world.World);

    var restoredTree = new TreeAttribute();
    restored.ToTreeAttributes(restoredTree);
    Assert.Equal(3, restoredTree.GetInt("count"));
  }
}
