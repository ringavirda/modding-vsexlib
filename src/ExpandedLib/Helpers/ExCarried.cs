using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace ExpandedLib.Helpers;

/// <summary>Counts and takes items a player carries, hotbar first and then backpack.</summary>
internal static class ExCarried {
  /// <summary>The number of items of <paramref name="code"/> in the hotbar and the backpack.</summary>
  /// <param name="player">The player whose inventories are read.</param>
  /// <param name="code">Item code.</param>
  public static int Count(IPlayer player, string code) {
    int total = 0;
    foreach (ItemSlot slot in Slots(player))
      if (Matches(slot, code))
        total += slot.StackSize;
    return total;
  }

  /// <summary>Takes <paramref name="count"/> items of <paramref name="code"/>, emptying the hotbar's slots before the backpack's.</summary>
  /// <param name="player">The player whose inventories are taken from.</param>
  /// <param name="code">Item code.</param>
  /// <param name="count">How many to take, at least 1.</param>
  /// <returns>The taken items as one stack of <paramref name="count"/> (the first slot's attributes), or null with nothing taken when fewer are carried.</returns>
  public static ItemStack? Take(IPlayer player, string code, int count) {
    if (Count(player, code) < count)
      return null;

    ItemStack? taken = null;
    int remaining = count;
    foreach (ItemSlot slot in Slots(player)) {
      if (remaining == 0)
        break;
      if (!Matches(slot, code))
        continue;
      ItemStack part = slot.TakeOut(remaining);
      slot.MarkDirty();
      remaining -= part.StackSize;
      if (taken == null)
        taken = part;
      else
        taken.StackSize += part.StackSize;
    }
    return taken;
  }

  private static bool Matches(ItemSlot slot, string code) =>
    slot.Itemstack?.Collectible.Code is { } found
    && WildcardUtil.Match(code, found.ToString());

  private static IEnumerable<ItemSlot> Slots(IPlayer player) {
    IPlayerInventoryManager manager = player.InventoryManager;
    foreach (
      IInventory? inventory in new[]
      {
        manager.GetHotbarInventory(),
        manager.GetOwnInventory(GlobalConstants.backpackInvClassName),
      }
    )
      if (inventory != null)
        foreach (ItemSlot slot in inventory)
          yield return slot;
  }
}
