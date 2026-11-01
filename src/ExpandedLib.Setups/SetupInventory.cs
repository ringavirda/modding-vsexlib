using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Scenarios;

/// <summary>The player inventory work the <c>fillInventory</c> step does in both setup runners.</summary>
internal static class SetupInventory {
  /// <summary>The numbered hotbar slots, from 0; the skill and off hand slots follow them.</summary>
  public const int HotbarSlots = 10;

  /// <summary>Fills the slots of <paramref name="player"/>'s hotbar (the ten numbered ones, the active slot
  /// included) and backpack that can hold <paramref name="item"/> with a full stack of it, replacing what
  /// they held, so the player takes no more of another item.</summary>
  /// <remarks>A slot with no inventory, as the harness's active slot, is filled unasked. The skill and off hand
  /// slots and the backpack's bag slots, which take only what they are made
  /// for, are left as they are. A player with no backpack inventory has its hotbar filled alone.</remarks>
  /// <param name="player">The player whose own inventories are filled.</param>
  /// <param name="item">The item or block each stack holds; its <see cref="CollectibleObject.MaxStackSize"/>
  /// sets the stack size.</param>
  /// <returns>The number of slots filled.</returns>
  public static int Fill(IPlayer player, CollectibleObject item) {
    IPlayerInventoryManager manager = player.InventoryManager;
    var slots = new List<ItemSlot>();
    if (
      manager.GetOwnInventory(GlobalConstants.hotBarInvClassName) is { } hotbar
    )
      for (int i = 0; i < HotbarSlots; i++)
        slots.Add(
          i == manager.ActiveHotbarSlotNumber
            ? manager.ActiveHotbarSlot
            : hotbar[i]
        );
    if (
      manager.GetOwnInventory(GlobalConstants.backpackInvClassName) is { } pack
    )
      slots.AddRange(pack);
    int filled = 0;
    foreach (ItemSlot slot in slots) {
      var stack = new ItemStack(item, item.MaxStackSize);
      if (slot.Inventory != null && !slot.CanHold(new DummySlot(stack)))
        continue;
      slot.Itemstack = stack;
      slot.MarkDirty();
      filled++;
    }
    return filled;
  }
}
