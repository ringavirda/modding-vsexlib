using ExpandedLib.Testing;
using NSubstitute;
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
    var pos = new BlockPos(0, 0, 0);
    world.Place(pos, block);
    var behavior = new BlockBehaviorWidget(block);

    behavior.GetPlacedBlockInfo(world.World, pos, world.Player().Player);

    // TestLang echoes a key back rather than formatting it, so the format argument the
    // behaviour passes is asserted through the substitute call, not the returned string.
    TestLang
      .Service.Received()
      .Get(
        "widgetdomain:widget-info",
        Arg.Is<object[]>(a => Equals(a[0], block.Code))
      );
  }
}
