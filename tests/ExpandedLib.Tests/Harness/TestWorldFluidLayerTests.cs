using System;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Each <see cref="TestWorld"/> cell holds a solid and a fluid layer, read and written per
/// <see cref="BlockLayersAccess"/> value as the engine's accessor does.</summary>
public class TestWorldFluidLayerTests {
  private static BlockPos Cell => new(4, 5, 6);

  #region Reads

  // Fails when Default reads the fluid layer first, Fluid reads the solid layer, FluidOrSolid
  // reads the solid layer first, or MostSolid answers liquid water.
  [Fact]
  public void A_cell_holding_both_layers_answers_each_layer_read() {
    var (world, water, _, stone) = World();
    world.Place(Cell, water).Place(Cell, stone);

    Assert.Same(stone, world.GetBlock(Cell));
    Assert.Same(stone, world.GetBlock(Cell, BlockLayersAccess.Default));
    Assert.Same(stone, world.GetBlock(Cell, BlockLayersAccess.SolidBlocks));
    Assert.Same(water, world.GetBlock(Cell, BlockLayersAccess.Fluid));
    Assert.Same(water, world.GetBlock(Cell, BlockLayersAccess.FluidOrSolid));
    Assert.Same(stone, world.GetBlock(Cell, BlockLayersAccess.MostSolid));
  }

  // Fails when SolidBlocks falls back to the fluid layer, or MostSolid answers liquid water.
  [Fact]
  public void A_cell_holding_only_water_reads_as_air_in_the_solid_reads() {
    var (world, water, _, _) = World();
    world.Place(Cell, water);

    Assert.Same(water, world.GetBlock(Cell));
    Assert.Same(world.Air, world.GetBlock(Cell, BlockLayersAccess.SolidBlocks));
    Assert.Same(water, world.GetBlock(Cell, BlockLayersAccess.FluidOrSolid));
    Assert.Same(world.Air, world.GetBlock(Cell, BlockLayersAccess.MostSolid));
  }

  // Fails when MostSolid skips ice in the fluid layer.
  [Fact]
  public void Ice_in_the_fluid_layer_is_the_most_solid_block() {
    var (world, _, ice, stone) = World();
    world.Place(Cell, ice).Place(Cell, stone);

    Assert.Same(ice, world.GetBlock(Cell, BlockLayersAccess.MostSolid));
  }

  // Fails when a read with an unknown layer answers a block.
  [Fact]
  public void A_read_of_no_layer_throws() {
    var (world, _, _, _) = World();

    Assert.Throws<ArgumentOutOfRangeException>(() => world.GetBlock(Cell, 9));
  }

  // Fails when the accessor's coordinate, raw or most-solid read ignores the layer.
  [Fact]
  public void The_accessor_reads_each_layer_through_every_overload() {
    var (world, water, _, stone) = World();
    world.Place(Cell, water).Place(Cell, stone);
    IBlockAccessor a = world.Accessor;

    Assert.Same(water, a.GetBlock(Cell, BlockLayersAccess.Fluid));
    Assert.Same(water, a.GetBlock(4, 5, 6, BlockLayersAccess.Fluid));
    Assert.Same(water, a.GetBlockRaw(4, 5, 6, BlockLayersAccess.Fluid));
    Assert.Same(stone, a.GetBlockRaw(4, 5, 6));
    world.Place(Cell, water);
    Assert.Same(world.Air, a.GetMostSolidBlock(Cell));
  }

  #endregion

  #region Writes

  // Fails when Place puts a fluid-layer block into the solid layer, or leaves the solid block and
  // its entity under it.
  [Fact]
  public void Placing_water_empties_the_solid_layer() {
    var (world, water, _, stone) = World();
    world.Place(Cell, stone, new PlainBe());

    world.Place(Cell, water);

    Assert.Same(world.Air, world.GetBlock(Cell, BlockLayersAccess.Solid));
    Assert.Same(water, world.GetBlock(Cell, BlockLayersAccess.Fluid));
    Assert.Null(world.GetBlockEntity(Cell));
  }

  // Fails when SetBlock without a layer puts a fluid-layer block into the solid layer.
  [Fact]
  public void SetBlock_without_a_layer_puts_water_in_the_fluid_layer() {
    var (world, water, _, stone) = World();
    world.Place(Cell, stone);

    world.Accessor.SetBlock(water.BlockId, Cell);

    Assert.Same(world.Air, world.GetBlock(Cell, BlockLayersAccess.Solid));
    Assert.Same(water, world.GetBlock(Cell, BlockLayersAccess.Fluid));
  }

  // Fails when clearing a cell without a layer clears its fluid too.
  [Fact]
  public void Clearing_a_cell_leaves_its_water() {
    var (world, water, _, stone) = World();
    world.Place(Cell, water).Place(Cell, stone);

    world.Accessor.SetBlock(0, Cell);

    Assert.Same(water, world.GetBlock(Cell));
  }

  // Fails when SetBlock with Solid writes the fluid layer, with Fluid writes nothing, or id 0 with
  // Fluid leaves the water.
  [Fact]
  public void SetBlock_with_a_layer_writes_that_layer_alone() {
    var (world, water, _, stone) = World();

    world.Accessor.SetBlock(water.BlockId, Cell, BlockLayersAccess.Fluid);
    world.Accessor.SetBlock(stone.BlockId, Cell, BlockLayersAccess.Solid);
    Assert.Same(stone, world.GetBlock(Cell, BlockLayersAccess.Solid));
    Assert.Same(water, world.GetBlock(Cell, BlockLayersAccess.Fluid));

    world.Accessor.SetBlock(0, Cell, BlockLayersAccess.Fluid);
    Assert.Same(world.Air, world.GetBlock(Cell, BlockLayersAccess.Fluid));
    Assert.Same(stone, world.GetBlock(Cell));
  }

  // Fails when SetBlock with a layer it cannot write does nothing.
  [Fact]
  public void SetBlock_with_a_read_only_layer_throws() {
    var (world, water, _, _) = World();

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      world.Accessor.SetBlock(
        water.BlockId,
        Cell,
        BlockLayersAccess.FluidOrSolid
      )
    );
  }

  #endregion

  #region World ticks

  // Fails when World's RegisterGameTickListener is not captured, or its unregister is not honoured.
  [Fact]
  public void A_tick_listener_registered_through_World_runs_at_its_interval_until_removed() {
    var world = new TestWorld();
    int fired = 0;
    long id = world.World.RegisterGameTickListener(_ => fired++, 20);

    world.AdvanceBlockEntityTime(100);
    Assert.Equal(5, fired);

    world.World.UnregisterGameTickListener(id);
    world.AdvanceBlockEntityTime(100);
    world.FireBlockEntityTicks();
    Assert.Equal(5, fired);
  }

  #endregion

  /// <summary>A world holding liquid water, ice and a stone, each a registered block.</summary>
  private static (TestWorld, Block, Block, Block) World() {
    var world = new TestWorld();
    Block water = TestBlocks.Configure(
      new BlockForFluidsLayer(),
      "test:water",
      71
    );
    water.MatterState = EnumMatterState.Liquid;
    Block ice = TestBlocks.Configure(new BlockForFluidsLayer(), "test:ice", 72);
    ice.MatterState = EnumMatterState.Solid;
    Block stone = TestBlocks.Configure(new Block(), "test:stone", 73);
    world.Register(water).Register(ice).Register(stone);
    return (world, water, ice, stone);
  }

  private sealed class PlainBe : BlockEntity { }
}
