using System.IO;
using System.Linq;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace SashWeights.Tests;

/// <summary>
/// The sample's box flask as the game loads it: a block declared in JSON alone that is exlib's sand
/// station, taken from ramming through the pour to the shake-out.
/// </summary>
public class BoxflaskCastingTests {
  private static readonly string Sample = Path.Combine(
    RepoPaths.Root,
    "samples",
    "SashWeights"
  );

  private static readonly BlockPos At = new(40, 100, 40, 0);

  private static ItemStack Stack(TestWorld world, string code, int size = 1) {
    var location = new AssetLocation(code);
    return world.Api.World.GetItem(location) is { } item
      ? new ItemStack(item, size)
      : new ItemStack(world.Api.World.GetBlock(location), size);
  }

  private static TestWorld Load() {
    var world = new TestWorld();
    world.LoadAsGame([Sample]);
    world.World.Side.Returns(EnumAppSide.Server);
    return world;
  }

  private static Block Boxflask(TestWorld world) =>
    world.Api.World.GetBlock(new AssetLocation("sashweights:boxflask"));

  private static BlockEntitySandStation Place(TestWorld world) {
    world.Api.World.BlockAccessor.SetBlock(Boxflask(world).BlockId, At);
    return Assert.IsType<BlockEntitySandStation>(world.GetBlockEntity(At));
  }

  [Fact]
  public void The_box_flask_is_the_bases_block_with_the_base_entity_and_a_molten_cell() {
    using TestWorld world = Load();

    Block block = Boxflask(world);
    BlockEntitySandStation station = Place(world);

    Assert.IsType<BlockSandStation>(block);
    Assert.Equal(
      "boxflask",
      block.Attributes["sandStation"]["station"].AsString()
    );
    Assert.Equal("boxflask", station.Kind?.Code);
    BEBehaviorMoltenCell cell = Assert.IsType<BEBehaviorMoltenCell>(
      station.Behaviors.Single(b => b is BEBehaviorMoltenCell)
    );
    Assert.Equal(60, cell.MaxUnitCapacity);
    Assert.Empty(
      world.Log.Errors.Where(e => e.Contains("invalid sand station block"))
    );
  }

  [Fact]
  public void Sand_pattern_pour_and_shake_out_give_four_sash_weights_and_no_scrap() {
    using TestWorld world = Load();
    BlockEntitySandStation station = Place(world);
    BEBehaviorMoltenCell cell = station.GetBehavior<BEBehaviorMoltenCell>();
    TestPlayer player = world.Player();

    Assert.True(player.Interact(At, Stack(world, "game:sand-granite")));
    Assert.Equal(SandStationLevel.Full, station.Sand);

    ItemStack pattern = Stack(world, "sashweights:pattern-sashweight");
    Assert.True(player.Interact(At, pattern));
    Assert.True(station.HasImpression);
    for (int i = 0; i < 3; i++)
      Assert.True(player.Interact(At, pattern));
    Assert.Equal(4, station.Pressed);
    Assert.Equal(60, cell.MaxUnitCapacity);

    var sink = (ILiquidMetalSink)station;
    int amount = 60;
    sink.BeginFill(new Vec3d(At.X + 0.5, At.Y + 0.5, At.Z + 0.5));
    sink.ReceiveLiquidMetal(Stack(world, "game:ingot-iron"), ref amount, 1500f);
    sink.OnPourOver();
    Assert.Equal(0, amount);
    Assert.Equal(60, cell.CellAmount);

    player.Hold(null);
    Assert.True(player.Interact(At, null));
    Assert.Empty(player.Hotbar.Where(s => s.Itemstack != null));

    ReflectionHelpers.Invoke(station, "OnServerTick", 1f);
    world.AdvanceHours(200);
    ReflectionHelpers.Invoke(station, "OnServerTick", 1f);
    Assert.True(cell.IsHardened);

    Assert.True(player.Interact(At, null));

    ItemStack[] given =
    [
      .. player.Hotbar.Select(s => s.Itemstack).OfType<ItemStack>(),
    ];
    ItemStack weights = Assert.Single(given);
    Assert.Equal("sashweights:sashweight", weights.Collectible.Code.ToString());
    Assert.Equal(4, weights.StackSize);
    Assert.Equal(SandStationLevel.Full, station.Sand);
    Assert.False(station.HasImpression);
  }
}
