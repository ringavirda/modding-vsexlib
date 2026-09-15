using System.Linq;
using BurdenMaker.BlockEntities;
using BurdenMaker.Blocks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace BurdenMaker.Tests;

/// <summary>
/// The block-placement path itself (<see cref="BlockFilledMegastructure.OnBlockPlaced"/>): a regression
/// guard for the owner's report of the machine vanishing right after placement. The vanish traced to the
/// polished shape's textures missing the game domain (fixed on dev before this branch), not to a removal
/// path in the placement code; this fact pins the placement code's own half of that guarantee - nothing
/// on <c>OnBlockPlaced</c>/<c>Initialize</c> removes or replaces the principal, and every footprint
/// filler lands with it.
/// </summary>
public class BurdenmakerPlacementTests {
  [Fact]
  public void Placing_the_burdenmaker_from_its_item_leaves_the_principal_and_every_filler_standing() {
    var world = new TestWorld();
    world.World.Side.Returns(EnumAppSide.Server);
    world.Register(world.Filler); // resolvable by StructureFillers under its own code

    var block = TestBlocks.Configure(
      new BlockBurdenmaker(),
      "burdenmaker:burdenmaker-red-n",
      140,
      ("brick", "red"),
      ("side", "n")
    );
    // Footprint read off the shipped def, as the other burdenmaker suites do.
    block.Attributes = new JsonObject(
      BlockBurdenmaker.Definitions("burdenmaker").First().ToJson()[
        "attributes"
      ]!
    );

    var pos = new BlockPos(64, 110, 64);
    var be = new BlockEntityBurdenmaker { Pos = pos, Block = block };
    world.Place(pos, block, be);
    world.Initialize(be); // runs Initialize, the other half of the owner's report

    // The item a player places from: an ItemStack wrapping the block itself, the same as vanilla's own
    // byItemStack at OnBlockPlaced.
    block.OnBlockPlaced(world.World, pos, new ItemStack(block));

    Assert.Same(block, world.GetBlock(pos));
    Assert.NotNull(world.GetBlockEntity(pos));

    var footprint = StructureFillers.FootprintCells(
      block,
      pos,
      block.StructureAngle
    );
    Assert.Equal(8, footprint.Count); // the 9-cell layout less the principal
    foreach (var cell in footprint)
      Assert.Equal(
        StructureFillers.FillerCode.ToString(),
        world.GetBlock(cell.Pos).Code.ToString()
      );
  }
}
