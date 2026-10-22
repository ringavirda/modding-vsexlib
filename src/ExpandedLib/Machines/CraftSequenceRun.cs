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
/// <param name="tools">Where the held tool's stroke defaults are read; <see cref="CraftSequenceRegistry.Shared"/>
/// when null.</param>
public sealed class CraftSequenceRun(
  CraftSequence sequence,
  CraftOrder order,
  IReadOnlyList<ItemSlot> slots,
  Random random,
  CraftSequenceRegistry? tools = null
)
{
  private readonly CraftSequenceRegistry _tools =
    tools ?? CraftSequenceRegistry.Shared;
  private string? _playing;
  private int _swings;
  private int _strikes;
  private int _hits;
  private int _done;

  /// <summary>The item attribute holding the chance, from 0 to 1, that a finished step costs the tool
  /// one durability point; a tool without it is worn by every step.</summary>
  public const string StepWearAttribute = CraftToolWear.StepWearAttribute;

  /// <summary>The step the player is on, from 0.</summary>
  public int Step { get; private set; }

  /// <summary>Seconds the current step has been held without a break.</summary>
  public float Held { get; private set; }

  /// <summary>Hits counted on the current step, from 0; they survive the player letting go and drop when
  /// the step ends or the bill is lifted.</summary>
  public int Struck => _hits;

  /// <summary>The step being worked.</summary>
  public CraftStep Current => sequence.Steps[Step];

  /// <summary>Drops the held time and returns to the first step.</summary>
  public void Reset()
  {
    Step = 0;
    Held = 0f;
    _swings = 0;
    _strikes = 0;
    _hits = 0;
    _done = 0;
  }

  /// <summary>Ends the animation a hold started; call when the player lets go. A timed step keeps its held
  /// time and strokes already played, so a later hold goes on from the same stroke; a step with
  /// <see cref="CraftStep.Hits"/> keeps the hits counted and drops the swing left unfinished.</summary>
  /// <param name="effects">The effects the holds played on; null for none.</param>
  public void Release(ICraftEffects? effects)
  {
    if (_playing != null)
      effects?.StopAnimation(_playing);
    _playing = null;
    if (Step < sequence.Steps.Length && Current.Hits > 0)
    {
      Held = 0f;
      _swings = 0;
      _strikes = 0;
      _done = _hits;
    }
  }

  // Ends the hold: the held time and strokes go; the hits counted stay when keepHits.
  private void Drop(ICraftEffects? effects, bool keepHits = false)
  {
    Release(effects);
    Held = 0f;
    _swings = 0;
    _strikes = 0;
    if (!keepHits)
      _hits = 0;
    _done = _hits;
  }

  /// <summary>
  /// Advances the run by <paramref name="dt"/> seconds of the player holding the current step; a hold
  /// whose bill or hands do not match does nothing, and the last step crafts. A step with
  /// <see cref="CraftStep.Hits"/> counts a hit at each finished swing and keeps them when the hold breaks.
  /// </summary>
  /// <param name="world">The world byproduct codes resolve in.</param>
  /// <param name="dt">Seconds held since the last call; 0 or more.</param>
  /// <param name="active">The player's active-hand slot; null or empty for bare hands.</param>
  /// <param name="offhand">The player's off-hand slot; null or empty for none.</param>
  /// <param name="viceOpen">Whether the station's vice is open.</param>
  /// <param name="effects">Receives the animation at each stroke of the step and the sound at each strike,
  /// from the step's own <see cref="CraftStep.Animation"/> and <see cref="CraftStep.Sound"/> or else the
  /// held tool's <see cref="CraftToolDefault"/>; null plays none.</param>
  /// <returns>What the hold did.</returns>
  public CraftSequenceProgress Hold(
    IWorldAccessor world,
    float dt,
    ItemSlot? active,
    ItemSlot? offhand,
    bool viceOpen,
    ICraftEffects? effects = null
  )
  {
    if (!BillLaid())
    {
      Release(effects);
      Reset();
      return CraftSequenceProgress.Idle;
    }

    CraftStep step = Current;
    if (!HandsMatch(step, active, offhand))
    {
      Drop(effects, true);
      return CraftSequenceProgress.Idle;
    }
    if (step.At == CraftStep.Vice && viceOpen)
    {
      Drop(effects, true);
      return CraftSequenceProgress.WaitingForVice;
    }

    Held += dt;
    CraftToolDefault? tool = _tools.ToolDefault(
      active?.Itemstack?.Collectible.Code.ToString()
    );
    Stroke(step, tool, effects);
    if (step.Hits > 0)
    {
      float swing = tool?.Stroke ?? CraftToolDefault.DefaultStroke;
      while (_hits < step.Hits && Held >= (_hits - _done + 1) * swing)
        _hits++;
      if (_hits < step.Hits)
        return CraftSequenceProgress.Working;
    }
    else if (Held < step.Seconds)
      return CraftSequenceProgress.Working;

    bool last = Step == sequence.Steps.Length - 1;
    ItemStack?[]? deposit = null;
    if (last)
    {
      deposit = Deposit(world);
      if (deposit == null)
      {
        if (step.Hits == 0)
          Held = step.Seconds;
        return CraftSequenceProgress.WaitingForOutput;
      }
    }

    Wear(step.Tool != null ? active : null);
    Wear(step.Offhand != null ? offhand : null);
    Drop(effects);

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

  // Starts the animation of each stroke and plays the sound of each strike that the held time has
  // reached, and none past the step's end: its seconds, or the swings its hits still need.
  private void Stroke(
    CraftStep step,
    CraftToolDefault? tool,
    ICraftEffects? effects
  )
  {
    if (effects == null)
      return;

    string? animation = step.Animation ?? tool?.Animation;
    string? sound = step.Sound ?? tool?.Sound;
    float stroke = tool?.Stroke ?? CraftToolDefault.DefaultStroke;
    float strike = tool?.Strike ?? CraftToolDefault.DefaultStrike;

    int left = step.Hits - _done;

    while (
      animation != null
      && (step.Hits == 0 || _swings < left)
      && Due(_swings * stroke, step)
    )
    {
      effects.StartAnimation(animation);
      _playing = animation;
      _swings++;
    }
    while (
      sound != null
      && (step.Hits == 0 || _strikes < left)
      && Due((_strikes * stroke) + strike, step)
    )
    {
      effects.Strike(sound);
      _strikes++;
    }
  }

  private bool Due(float at, CraftStep step) =>
    at <= Held && (step.Hits > 0 || at < step.Seconds);

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

  private void Wear(ItemSlot? slot) => CraftToolWear.Apply(slot, random);

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
