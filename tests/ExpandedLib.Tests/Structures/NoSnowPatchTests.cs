using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The snow postfixes on <see cref="Block.AllowSnowCoverage"/> and
/// <see cref="Block.GetSnowCoveredVariant"/>: refused at a <see cref="NoSnowCells"/> cell, vanilla one
/// cell away.</summary>
[Collection(ExHarmonyCollection.Name)]
public class NoSnowPatchTests {
  private static HarmonyFixture Patched() =>
    new("exlibtest.nosnow", typeof(NoSnowCells).Assembly);

  // Fails when the AllowSnowCoverage postfix is not applied.
  [Fact]
  public void A_marked_solid_block_takes_no_snow_layer_and_its_neighbour_does() {
    using var fixture = Patched();
    var block = new Block();
    var marked = new BlockPos(8000, 10, 0);
    object owner = new();
    NoSnowCells.Mark(owner, [marked]);
    try {
      Assert.False(block.AllowSnowCoverage(null!, marked));
      Assert.True(block.AllowSnowCoverage(null!, marked.EastCopy()));
    } finally {
      NoSnowCells.Unmark(owner);
    }
  }

  // Fails when the GetSnowCoveredVariant postfix is not applied.
  [Fact]
  public void A_marked_block_stays_itself_and_its_neighbour_turns_snowy() {
    using var fixture = Patched();
    var snowy = new Block();
    var block = new Block { snowCovered1 = snowy };
    block.notSnowCovered = block;
    var marked = new BlockPos(8100, 10, 0);
    object owner = new();
    NoSnowCells.Mark(owner, [marked]);
    try {
      Assert.Same(block, block.GetSnowCoveredVariant(marked, 2));
      Assert.Same(snowy, block.GetSnowCoveredVariant(marked.EastCopy(), 2));
    } finally {
      NoSnowCells.Unmark(owner);
    }
  }

  // Fails when the postfix is not applied or the structure never fills the registry.
  [Fact]
  public void A_formed_structure_refuses_snow_on_its_floor_cell() {
    using var fixture = Patched();
    var anchor = new BlockPos(8200, 10, 0);
    var (world, _) = NoSnowCellsTests.Formed(anchor);
    var brick = new Block();

    Assert.False(brick.AllowSnowCoverage(world.World, anchor.AddCopy(2, 0, 0)));
    Assert.True(brick.AllowSnowCoverage(world.World, anchor.AddCopy(1, 0, 0)));
  }

  // Fails when Clear leaves marks behind.
  [Fact]
  public void Clearing_drops_every_owners_marks() {
    var cell = new BlockPos(8300, 10, 0);
    NoSnowCells.Mark(new object(), [cell]);

    NoSnowCells.Clear();

    Assert.False(NoSnowCells.IsMarked(cell));
  }
}
