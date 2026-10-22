using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="SetupInventory.Fill"/>: every slot of the player's inventories full of one item.</summary>
public class SetupInventoryTests {
  private const int BackpackSlots = 4;

  private static (
    TestWorld World,
    TestPlayer Player,
    Item Plank,
    Item Ingot
  ) Stand() {
    var world = new TestWorld();
    TestPlayer player = world.Player();
    Item plank = world.RegisterItem("game:plank-oak");
    plank.MaxStackSize = 64;
    Item ingot = world.RegisterItem("game:ingot-iron");
    ingot.MaxStackSize = 64;
    return (world, player, plank, ingot);
  }

  // Fails when the fill leaves the last numbered hotbar slot or the active slot empty, so one more stack is taken.
  [Fact]
  public void FillInventory_HotbarFull_RefusesAnotherItem() {
    var (_, player, plank, ingot) = Stand();

    int filled = SetupInventory.Fill(player.Player, plank);

    Assert.Equal(SetupInventory.HotbarSlots, filled);
    for (int i = 0; i < SetupInventory.HotbarSlots; i++)
      Assert.Equal(64, player.StackInSlot(i)?.StackSize);
    var more = new ItemStack(ingot, 1);
    Assert.False(player.Player.InventoryManager.TryGiveItemstack(more));
    Assert.Equal(1, more.StackSize);
  }

  // Fails when the fill skips the backpack inventory, so a stack still fits there.
  [Fact]
  public void FillInventory_WithABackpack_FillsItToo() {
    var (world, player, plank, ingot) = Stand();
    InventoryGeneric backpack = TestInventory.Of(
      world,
      BackpackSlots,
      $"{GlobalConstants.backpackInvClassName}-test"
    );
    player.Player.InventoryManager.OpenInventory(backpack);

    int filled = SetupInventory.Fill(player.Player, plank);

    Assert.Equal(SetupInventory.HotbarSlots + BackpackSlots, filled);
    Assert.All(backpack, slot => Assert.Equal(64, slot.Itemstack?.StackSize));
    Assert.False(
      backpack.Any(s =>
        s.CanHold(new DummySlot(new ItemStack(ingot, 1))) && s.Empty
      )
    );
  }
}
