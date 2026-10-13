using System.Collections.Generic;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>What <see cref="TestWorld"/>'s accessor runs when a stack places a block.</summary>
public class TestWorldPlacementTests {
  private static BlockPos Cell => new(1, 2, 3);

  private sealed class RecordingBlock(bool fluid) : Block {
    public readonly List<ItemStack?> Stacks = [];

    public override bool ForFluidsLayer => fluid;

    public override void OnBlockPlaced(
      IWorldAccessor world,
      BlockPos blockPos,
      ItemStack? byItemStack = null
    ) => Stacks.Add(byItemStack);
  }

  private static RecordingBlock Placed(TestWorld world, bool fluid = false) {
    var block = TestBlocks.Configure(
      new RecordingBlock(fluid),
      "test:recorder",
      7
    );
    world.Register(block);
    return block;
  }

  // Fails when the stack overload of SetBlock drops the stack or never runs OnBlockPlaced.
  [Fact]
  public void SetBlock_with_a_stack_runs_OnBlockPlaced_with_that_stack() {
    var world = new TestWorld();
    RecordingBlock block = Placed(world);
    var stack = new ItemStack(block);

    world.Accessor.SetBlock(block.BlockId, Cell, stack);

    Assert.Same(block, world.GetBlock(Cell));
    Assert.Same(stack, Assert.Single(block.Stacks));
  }

  // Fails when a fluid-layer block placed with a stack runs OnBlockPlaced as a solid one does.
  [Fact]
  public void SetBlock_with_a_stack_leaves_a_fluid_layer_block_unplaced() {
    var world = new TestWorld();
    RecordingBlock block = Placed(world, fluid: true);

    world.Accessor.SetBlock(block.BlockId, Cell, new ItemStack(block));

    Assert.Empty(block.Stacks);
  }

  private sealed class StackEntity : BlockEntity {
    public readonly List<ItemStack?> Stacks = [];

    public override void OnBlockPlaced(ItemStack? byItemStack = null) =>
      Stacks.Add(byItemStack);
  }

  // Fails when the stack overload spawns the block entity itself and the block's OnBlockPlaced
  // spawns a second: two entities are created and the stack reaches only one of them.
  [Fact]
  public void SetBlock_with_a_stack_spawns_the_block_entity_once() {
    var world = new TestWorld();
    var spawned = new List<StackEntity>();
    world.RegisterBlockEntityFactory(
      "stackentity",
      () => {
        var be = new StackEntity();
        spawned.Add(be);
        return be;
      }
    );
    var block = TestBlocks.Configure(new Block(), "test:withentity", 8);
    block.EntityClass = "stackentity";
    world.Register(block);
    var stack = new ItemStack(block);

    world.Accessor.SetBlock(block.BlockId, Cell, stack);

    StackEntity only = Assert.Single(spawned);
    Assert.Same(only, world.GetBlockEntity(Cell));
    Assert.Same(stack, Assert.Single(only.Stacks));
  }
}
