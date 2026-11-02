using ExpandedLib.Catalogues;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>The wear a finished machining job charges the cutting tool fitted to its machine.</summary>
public static class MachineToolWear {
  /// <summary>Takes <paramref name="points"/> of durability from the tool in <paramref name="slot"/>; at 0 or
  /// less the tool is removed and the drop its <c>machinetool.spent</c> names is returned. The slot is marked
  /// dirty when anything changed.</summary>
  /// <param name="slot">The slot the tool is fitted in. Null, empty, or holding an item without durability: nothing changes.</param>
  /// <param name="points">Durability to take. 0 or less changes nothing.</param>
  /// <returns>The spent drop when the tool wore out and names one that resolves in the slot's world; otherwise null.</returns>
  public static ItemStack? Apply(ItemSlot? slot, int points) {
    ItemStack? stack = slot?.Itemstack;
    if (
      slot == null
      || stack == null
      || points <= 0
      || stack.Collectible.Durability <= 0
    )
      return null;

    int left = stack.Collectible.GetRemainingDurability(stack) - points;
    if (left > 0) {
      stack.Attributes.SetInt("durability", left);
      slot.MarkDirty();
      return null;
    }

    MachineTool.TryParse(
      stack.Collectible.Attributes?[MachineTool.AttributeKey],
      out MachineToolSpec spec,
      out _
    );
    slot.Itemstack = null;
    slot.MarkDirty();
    if (spec.SpentCode == null)
      return null;

    IWorldAccessor? world = slot.Inventory?.Api?.World;
    var location = new AssetLocation(spec.SpentCode);
    CollectibleObject? drop =
      (CollectibleObject?)world?.GetItem(location) ?? world?.GetBlock(location);
    return drop == null ? null : new ItemStack(drop, spec.SpentCount);
  }
}
