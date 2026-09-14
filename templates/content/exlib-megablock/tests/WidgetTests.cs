using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using WidgetNamespace.BlockEntities;
using WidgetNamespace.Blocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>The megablock's footprint, and that a click on a rim cell reaches the principal via the
/// filler.</summary>
public class WidgetTests {
  private static JObject Attributes() {
    ExBlockDef def = BlockWidget.Definitions("widgetdomain").Single();
    return (JObject)def.ToJson()["attributes"]!;
  }

  private static BlockWidget Widget(string side, int id) {
    var block = TestBlocks.Configure(
      new BlockWidget(),
      $"widgetdomain:widget-{side}",
      id,
      ("side", side)
    );
    block.Attributes = new JsonObject(Attributes());
    return block;
  }

  [Fact]
  public void The_footprint_reserves_the_eight_rim_cells() {
    BlockWidget block = Widget("n", 1);
    var hub = new BlockPos(0, 0, 0);
    var cells = StructureFillers.FootprintCells(
      block,
      hub,
      block.StructureAngle
    );
    Assert.Equal(8, cells.Count);

    var offsets = cells
      .Select(c => (c.Pos.X - hub.X, c.Pos.Y - hub.Y, c.Pos.Z - hub.Z))
      .ToHashSet();
    var expected = new HashSet<(int, int, int)>();
    for (int x = -1; x <= 1; x++)
      for (int y = -1; y <= 1; y++)
        if (x != 0 || y != 0)
          expected.Add((x, y, 0));
    Assert.Equal(expected, offsets);
  }

  [Fact]
  public void A_click_on_a_rim_cell_reaches_the_principal_and_is_counted() {
    var world = new TestWorld();
    world.World.Side.Returns(EnumAppSide.Server);
    var hub = new BlockPos(0, 0, 0);
    BlockWidget block = Widget("n", 1);
    var be = new BlockEntityWidget();
    world.Place(hub, block, be);
    world.Initialize(be);

    var rimCell = new BlockPos(1, 0, 0);
    world.PlaceFiller(rimCell, principal: hub);

    TestPlayer player = world.Player();
    var selection = new BlockSelection { Position = rimCell };
    bool handled = world.Filler.OnBlockInteractStart(
      world.World,
      player.Player,
      selection
    );

    Assert.True(handled);

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);
    Assert.Equal(1, tree.GetInt("cells"));
  }
}
