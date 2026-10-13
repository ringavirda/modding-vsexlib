#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What a setup's readings see of a world's molten cells.</summary>
public class SetupWorldViewTests
{
  // Fails when the view reads the molten cell behaviour only, so a block entity that is the cell
  // itself reads as holding nothing.
  [Fact]
  public void A_block_entity_that_is_a_molten_cell_reads_what_it_holds()
  {
    var world = new TestWorld();
    var at = new BlockPos(2, 1, 3);
    var cell = new SetupMoltenEntity
    {
      CellAmount = 37,
      CellMetalType = "game:ingot-iron",
      CellTemperature = 1650f,
    };
    world.Attach(cell);
    world.Place(at, TestBlocks.Configure(new Block(), "test:canal", 5), cell);
    var view = new SetupWorldView(
      world,
      new Dictionary<string, SetupPlacement>()
    );

    MoltenCellReading? reading = view.MoltenCellAt(at);

    Assert.Equal(new MoltenCellReading(37, "game:ingot-iron", 1650f), reading);
  }
}
#endif
