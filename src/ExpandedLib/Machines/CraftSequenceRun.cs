#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace ExpandedLib.Machines;

/// <summary>What one <see cref="CraftSequenceRun.Hold"/> did.</summary>
public enum CraftSequenceProgress
{
  /// <summary>The bill is not laid, or the hands do not match the current step; nothing advanced.</summary>
  Idle,

  /// <summary>The step is being held.</summary>
  Working,

  /// <summary>The step is worked at the vice and the vice is open; nothing advanced.</summary>
  WaitingForVice,

  /// <summary>The last step is held for its time but the outputs cannot take the product and
  /// byproducts; the craft finishes on the first hold that finds room.</summary>
  WaitingForOutput,

  /// <summary>A step finished and another follows.</summary>
  StepDone,

  /// <summary>The last step finished: the bill is consumed and the product and byproducts are in the
  /// outputs. The run is back at its first step.</summary>
  Crafted,
}

/// <summary>
/// The labour a station drives for one <see cref="CraftSequence"/>: step index, held time, tool wear
/// and the bill, product and byproducts at the last step. World state lives in the slots.
/// </summary>
/// <param name="sequence">The sequence worked.</param>
/// <param name="order">The order whose <see cref="CraftOrder.Inputs"/> are the bill and whose
/// <see cref="CraftOrder.OutputSlots"/> and <see cref="CraftOrder.Product"/> receive the result.</param>
/// <param name="slots">The station's inventory slots, the indices the order names.</param>
/// <param name="random">Rolls the wear chance of each finished step.</param>
public sealed class CraftSequenceRun(
  CraftSequence sequence,
  CraftOrder order,
  IReadOnlyList<ItemSlot> slots,
  Random random
)
{
  /// <summary>The item attribute holding the chance, from 0 to 1, that a finished step costs the tool
  /// one durability point; a tool without it is worn by every step.</summary>
  public const string StepWearAttribute = "stepWear";

  /// <summary>The step the player is on, from 0.</summary>
  public int Step { get; private set; }

  /// <summary>Seconds the current step has been held without a break.</summary>
  public float Held { get; private set; }

  /// <summary>The step being worked.</summary>
  public CraftStep Current => sequence.Steps[Step];

  /// <summary>Drops the held time and returns to the first step.</summary>
  public void Reset()
  {
    Step = 0;
    Held = 0f;
  }

  /// <summary>
  /// Advances the run by <paramref name="dt"/> seconds of the player holding the current step; a hold
  /// whose bill or hands do not match does nothing, and the last step crafts.
  /// </summary>
  /// <param name="world">The world byproduct codes resolve in.</param>
  /// <param name="dt">Seconds held since the last call; 0 or more.</param>
  /// <param name="active">The player's active-hand slot; null or empty for bare hands.</param>
  /// <param name="offhand">The player's off-hand slot; null or empty for none.</param>
  /// <param name="viceOpen">Whether the station's vice is open.</param>
  /// <returns>What the hold did.</returns>
  public CraftSequenceProgress Hold(
    IWorldAccessor world,
    float dt,
    ItemSlot? active,
    ItemSlot? offhand,
    bool viceOpen
  )
  {
    if (!BillLaid())
    {
      Reset();
      return CraftSequenceProgress.Idle;
    }

    CraftStep step = Current;
    if (!HandsMatch(step, active, offhand))
    {
      Held = 0f;
      return CraftSequenceProgress.Idle;
    }
    if (step.At == CraftStep.Vice && viceOpen)
    {
      Held = 0f;
      return CraftSequenceProgress.WaitingForVice;
    }

    Held += dt;
    if (Held < step.Seconds)
      return CraftSequenceProgress.Working;

    bool last = Step == sequence.Steps.Length - 1;
    ItemStack?[]? deposit = null;
    if (last)
    {
      deposit = Deposit(world);
      if (deposit == null)
      {
        Held = step.Seconds;
        return CraftSequenceProgress.WaitingForOutput;
      }
    }

    Wear(step.Tool != null ? active : null);
    Wear(step.Offhand != null ? offhand : null);
    Held = 0f;

    if (!last)
    {
      Step++;
      return CraftSequenceProgress.StepDone;
    }

    ConsumeBill();
    Place(deposit!);
    Step = 0;
    return CraftSequenceProgress.Crafted;
  }

  private bool BillLaid() =>
    order.Inputs.All(input =>
    {
      ItemStack? stack = slots[input.Slot].Itemstack;
      return stack != null
        && stack.StackSize >= input.Ingredient.Quantity
        && input.Ingredient.SatisfiesAsIngredient(stack);
    });

  private static bool HandsMatch(
    CraftStep step,
    ItemSlot? active,
    ItemSlot? offhand
  ) =>
    (
      step.Tool == null
        ? active?.Itemstack == null
        : Matches(step.Tool, active?.Itemstack)
    ) && (step.Offhand == null || Matches(step.Offhand, offhand?.Itemstack));

  private static bool Matches(string pattern, ItemStack? stack) =>
    stack != null
    && WildcardUtil.Match(pattern, stack.Collectible.Code.ToString());

  private void Wear(ItemSlot? slot)
  {
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

  private void ConsumeBill()
  {
    foreach (CraftInput input in order.Inputs)
    {
      if (input.Ingredient.IsTool)
        continue;
      ItemSlot slot = slots[input.Slot];
      slot.TakeOut(input.Ingredient.Quantity);
      slot.MarkDirty();
    }
  }

  // The output stacks as they would stand with the product and byproducts added, one per output
  // slot index in OutputSlots order; null when they do not all fit.
  private ItemStack?[]? Deposit(IWorldAccessor world)
  {
    var wanted = new List<ItemStack> { order.Product.Clone() };
    foreach (CraftByproduct by in sequence.Byproducts)
    {
      var code = new AssetLocation(by.Code);
      CollectibleObject? shed =
        by.Type == "block" ? world.GetBlock(code) : world.GetItem(code);
      if (shed == null)
      {
        world.Logger.Error(
          "Craft sequence {0}: byproduct {1} is no loaded {2}; the craft sheds none of it.",
          sequence.Recipe,
          by.Code,
          by.Type
        );
        continue;
      }
      wanted.Add(new ItemStack(shed, by.Quantity));
    }

    ItemStack?[] after =
    [
      .. order.OutputSlots.Select(i => slots[i].Itemstack?.Clone()),
    ];
    foreach (ItemStack stack in wanted)
    {
      int left = stack.StackSize;
      for (int i = 0; i < after.Length && left > 0; i++)
      {
        ItemStack? held = after[i];
        if (
          held == null
          || !held.Equals(world, stack, GlobalConstants.IgnoredStackAttributes)
        )
          continue;
        int room =
          Math.Min(
            held.Collectible.MaxStackSize,
            slots[order.OutputSlots[i]].MaxSlotStackSize
          ) - held.StackSize;
        int moved = Math.Min(room, left);
        held.StackSize += moved;
        left -= moved;
      }
      for (int i = 0; i < after.Length && left > 0; i++)
      {
        if (after[i] != null)
          continue;
        int moved = Math.Min(stack.Collectible.MaxStackSize, left);
        after[i] = stack.Clone();
        after[i]!.StackSize = moved;
        left -= moved;
      }
      if (left > 0)
        return null;
    }
    return after;
  }

  private void Place(ItemStack?[] deposit)
  {
    for (int i = 0; i < deposit.Length; i++)
    {
      ItemSlot slot = slots[order.OutputSlots[i]];
      slot.Itemstack = deposit[i];
      slot.MarkDirty();
    }
  }
}
#endif
