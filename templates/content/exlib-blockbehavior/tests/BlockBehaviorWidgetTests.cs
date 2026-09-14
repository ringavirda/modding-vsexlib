using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using WidgetNamespace.Behaviors;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>The behaviour names its block's code in the placed-block info.</summary>
public class BlockBehaviorWidgetTests {
  public BlockBehaviorWidgetTests() => TestLang.Init();

  [Fact]
  public void Names_the_blocks_code_in_the_placed_block_info() {
    var world = new TestWorld();
    Block block = TestBlocks.Configure(new Block(), "widgetdomain:widget", 1);
    var behavior = new BlockBehaviorWidget(block);

    string info = behavior.GetPlacedBlockInfo(
      world.World,
      new BlockPos(0, 0, 0),
      world.Player().Player
    );

    Assert.Equal("widgetdomain:widget-info", info);
  }
}
