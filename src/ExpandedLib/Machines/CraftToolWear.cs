using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>The wear a finished craft step or construction stage charges the tool it is worked with.</summary>
internal static class CraftToolWear {
  /// <summary>The item attribute holding the chance, from 0 to 1, that a finished step costs the tool
  /// one durability point; a tool without it is worn by every step.</summary>
  internal const string StepWearAttribute = "stepWear";

  /// <summary>Rolls the tool's wear chance and, on a hit, takes one durability point; the stack is
  /// removed when none is left. A slot that is null or empty, or a tool without durability, is left
  /// alone.</summary>
  /// <param name="slot">The slot the tool is held in.</param>
  /// <param name="random">Rolls the wear chance.</param>
  internal static void Apply(ItemSlot? slot, Random random) {
    ItemStack? stack = slot?.Itemstack;
    if (slot == null || stack == null || stack.Collectible.Durability <= 0)
      return;

    float chance =
      stack.Collectible.Attributes?[StepWearAttribute].AsFloat(1f) ?? 1f;
    if (random.NextDouble() >= chance)
      return;

    int left = stack.Collectible.GetRemainingDurability(stack) - 1;
    if (left <= 0)
      slot.Itemstack = null;
    else
      stack.Attributes.SetInt("durability", left);
    slot.MarkDirty();
  }
}
