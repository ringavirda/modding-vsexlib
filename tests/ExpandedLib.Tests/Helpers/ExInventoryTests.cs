using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The player-inventory queries machine costs share: counting and taking matching items over the
/// whole inventory (<c>Entity.WalkInventory</c>) versus the hotbar only
/// (<c>InventoryManager.GetHotbarInventory</c>). "Matching" is always the caller's own predicate;
/// these tests use item code as a stand-in.
/// </summary>
public class ExInventoryTests {
  private static ItemStack Stack(string code, int size) =>
    new(new Item { Code = new AssetLocation(code) }, size);

  private static bool IsIron(ItemStack stack) =>
    stack.Collectible.Code.Path.Contains("iron");

  /// <summary>A player whose <c>Entity.WalkInventory</c> walks exactly <paramref name="slots"/>,
  /// in order, honouring the handler's early-stop return.</summary>
  private static IPlayer PlayerOver(params ItemSlot[] slots) {
    TestPlayer player = new TestWorld().Player();
    player
      .Entity.When(e => e.WalkInventory(Arg.Any<OnInventorySlot>()))
      .Do(ci => {
        var handler = ci.Arg<OnInventorySlot>();
        foreach (var slot in slots)
          if (!handler(slot))
            break;
      });
    return player.Player;
  }

  private static IPlayer PlayerWithHotbar(params ItemSlot[] slots) {
    var inv = Substitute.For<IInventory>();
    inv.GetEnumerator().Returns(_ => slots.AsEnumerable().GetEnumerator());
    TestPlayer player = new TestWorld().Player();
    player.Player.InventoryManager.GetHotbarInventory().Returns(inv);
    return player.Player;
  }

  [Fact]
  public void Count_sums_the_stack_sizes_of_matching_slots_only() {
    IPlayer player = PlayerOver(
      new DummySlot(Stack("game:ingot-iron", 5)),
      new DummySlot(Stack("game:ingot-copper", 3)),
      new DummySlot(Stack("game:ingot-iron", 2)),
      new DummySlot(null!)
    );

    Assert.Equal(7, ExInventory.Count(player, IsIron));
  }

  [Fact]
  public void Take_removes_up_to_the_requested_quantity_across_slots() {
    var iron1 = new DummySlot(Stack("game:ingot-iron", 5));
    var iron2 = new DummySlot(Stack("game:ingot-iron", 5));
    IPlayer player = PlayerOver(iron1, iron2);

    int taken = ExInventory.Take(player, IsIron, 7);

    Assert.Equal(7, taken);
    Assert.Equal(0, iron1.Itemstack?.StackSize ?? 0);
    Assert.Equal(3, iron2.Itemstack!.StackSize);
  }

  [Fact]
  public void Take_returns_less_than_requested_when_the_inventory_runs_out() {
    IPlayer player = PlayerOver(new DummySlot(Stack("game:ingot-iron", 2)));

    int taken = ExInventory.Take(player, IsIron, 10);

    Assert.Equal(2, taken);
  }

  [Fact]
  public void CountHotbar_only_sums_the_hotbar_not_the_wider_inventory() {
    IPlayer player = PlayerWithHotbar(
      new DummySlot(Stack("game:ingot-iron", 4)),
      new DummySlot(Stack("game:ingot-copper", 9))
    );

    Assert.Equal(4, ExInventory.CountHotbar(player, IsIron));
  }

  [Fact]
  public void CountHotbar_is_zero_when_there_is_no_hotbar_inventory() {
    TestPlayer player = new TestWorld().Player();
    player
      .Player.InventoryManager.GetHotbarInventory()
      .Returns((IInventory?)null);

    Assert.Equal(0, ExInventory.CountHotbar(player.Player, IsIron));
  }

  [Fact]
  public void TakeHotbar_removes_up_to_the_requested_quantity_from_the_hotbar() {
    var slot = new DummySlot(Stack("game:ingot-iron", 5));
    IPlayer player = PlayerWithHotbar(slot);

    int taken = ExInventory.TakeHotbar(player, IsIron, 3);

    Assert.Equal(3, taken);
    Assert.Equal(2, slot.Itemstack!.StackSize);
  }

  [Fact]
  public void TakeHotbar_is_zero_when_there_is_no_hotbar_inventory() {
    TestPlayer player = new TestWorld().Player();
    player
      .Player.InventoryManager.GetHotbarInventory()
      .Returns((IInventory?)null);

    Assert.Equal(0, ExInventory.TakeHotbar(player.Player, IsIron, 5));
  }
}
