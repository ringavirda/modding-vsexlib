using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using WidgetNamespace.BlockEntities;
using WidgetNamespace.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>The widget core: it only ticks while its structure is complete, and loses completion the
/// moment a wall cell is broken.</summary>
public class WidgetTests {
  private sealed record Rig(TestWorld World, BlockEntityWidget Widget, StructureRig Structure);

  private static Rig Build() {
    var world = new TestWorld();
    var widget = new BlockEntityWidget();
    Block block = TestBlocks.Configure(
      new BlockWidget(),
      "widgetdomain:widget-n",
      1,
      ("side", "n")
    );
    world.Place(new BlockPos(10, 10, 10), block, widget);

    ExBlockDef def = BlockWidget.Definitions("widgetdomain").Single();
    StructureRig structure = StructureRig.Around(world, widget, def, angle: 0);

    return new Rig(world, widget, structure);
  }

  [Fact]
  public void An_unraised_rig_never_completes_and_never_ticks() {
    Rig rig = Build();
    rig.World.Initialize(rig.Widget);

    rig.World.AdvanceBlockEntityTime(3000);

    Assert.False(rig.Widget.StructureComplete);
  }

  [Fact]
  public void Breaking_a_wall_cell_drops_completion_on_the_next_monitor_tick() {
    Rig rig = Build();
    rig.Structure.Complete();

    BlockPos wall = rig.Structure.Cell(-1, 0, -1);
    rig.World.Place(wall, rig.World.Air);
    rig.World.AdvanceBlockEntityTime(3000);

    Assert.False(rig.Widget.StructureComplete);
  }

  [Fact]
  public void Ticks_round_trip_through_the_tree() {
    Rig rig = Build();
    rig.Structure.Complete();
    for (int i = 0; i < 3; i++) {
      rig.World.FireBlockEntityTicks();
      rig.World.Tick(1);
    }

    var tree = new TreeAttribute();
    rig.Widget.ToTreeAttributes(tree);
    Assert.Equal(3, tree.GetInt("ticks"));

    var restored = new BlockEntityWidget {
      Pos = rig.Widget.Pos,
      Block = rig.Widget.Block,
    };
    restored.FromTreeAttributes(tree, rig.World.World);

    var restoredTree = new TreeAttribute();
    restored.ToTreeAttributes(restoredTree);
    Assert.Equal(3, restoredTree.GetInt("ticks"));
  }
}
