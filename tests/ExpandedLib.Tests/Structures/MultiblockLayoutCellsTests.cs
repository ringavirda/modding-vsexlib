using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A multiblock's layout cells at its placed facing: each authored offset beside the world
/// cell its completion check reads and the code it wants there, and the peripheral cell the entity
/// resolves the same offset to.</summary>
public class MultiblockLayoutCellsTests {
  private static readonly BlockPos At = new(0, 10, 0);

  private static TestMegablock Stand(int angle) {
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = angle };
    world.Place(
      At,
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);
    StructureRig.Around(
      world,
      machine,
      ExBlockDef
        .Create("exlib", "testmega")
        .Multiblock(m =>
          m.Number("exlib:testmega*", 1)
            .Number("exlib:testbrick*", 2)
            .At(0, 0, 0, 1)
            .At(1, 0, 0, 2)
            .At(0, 1, 2, 2)
        ),
      angle
    );
    return machine;
  }

  // Fails when LayoutCells places a cell at its authored offset or pairs it with another offset.
  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void Each_authored_offset_sits_where_the_facing_turns_it(int angle) {
    TestMegablock machine = Stand(angle);

    Assert.Equal(
      [
        ((0, 0, 0), At, "exlib:testmega*"),
        (
          (1, 0, 0),
          ExOrientation.GlobalPos(At, 1, 0, 0, angle),
          "exlib:testbrick*"
        ),
        (
          (0, 1, 2),
          ExOrientation.GlobalPos(At, 0, 1, 2, angle),
          "exlib:testbrick*"
        ),
      ],
      machine.LayoutCells.Select(c => (c.Local, c.At, c.Wanted.ToString()))
    );
  }

  // Fails when PeripheralCell resolves through another frame than GetGlobalPos.
  [Fact]
  public void A_peripheral_resolves_to_the_cell_the_layout_turns_it_to() {
    TestMegablock machine = Stand(90);

    Assert.All(
      machine.LayoutCells,
      c =>
        Assert.Equal(
          c.At,
          machine.PeripheralCell(c.Local.X, c.Local.Y, c.Local.Z)
        )
    );
    Assert.NotEqual(At.AddCopy(1, 0, 0), machine.PeripheralCell(1, 0, 0));
  }
}
