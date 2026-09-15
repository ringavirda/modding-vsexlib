using System.Collections.Generic;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Metals;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// Coarse thermal state of a metal stack relative to its melting point.
/// </summary>
public enum MoltenState {
  /// <summary>Above the liquid threshold (default 80% of the melting point): flows freely.</summary>
  Liquid,

  /// <summary>Between the hardened and liquid thresholds: no longer flows, still hot.</summary>
  Cooling,

  /// <summary>Below the hardened threshold (default 30% of the melting point): chisellable.</summary>
  Hardened,
}

/// <summary>
/// Single source of truth for treating an <see cref="ItemStack"/> as a carrier of molten metal:
/// creating the temperature-tracked stack, reading/writing temperature, classifying thermal state,
/// the incandescent block-light scale, and player-facing metal/state formatting.
/// </summary>
public static class MoltenMetal {
  /// <summary>Fraction of the melting point above which metal counts as liquid.</summary>
  public static float LiquidThreshold => ExlibValues.MetalLiquidThreshold;

  /// <summary>Fraction of the melting point below which metal counts as fully hardened.</summary>
  public static float HardenedThreshold => ExlibValues.MetalHardenedThreshold;

  /// <summary>Below this temperature ( deg C) hot metal emits no block light.</summary>
  public static float GlowMinTemp => ExlibValues.MetalGlowMinTemp;

  /// <summary>Creates a single-item temperature carrier for <paramref name="itemCode"/> at
  /// <paramref name="temperature"/> deg C; returns <c>null</c> when the item does not resolve.</summary>
  public static ItemStack? CreateStack(
    IWorldAccessor world,
    string itemCode,
    float temperature,
    float? cooldownSpeed = null
  ) {
    Item? item =
      itemCode.Length > 0 ? world.GetItem(new AssetLocation(itemCode)) : null;
    if (item == null)
      return null;
    var stack = new ItemStack(item, 1);
    // SetTemperature first: creates the "temperature" tree SetCooldownSpeed writes into.
    SetTemperature(world, stack, temperature);
    SetCooldownSpeed(stack, cooldownSpeed ?? ExlibValues.MoltenCooldownDefault);
    return stack;
  }

  /// <summary>Sets the VS time-based cooldown speed on an existing temperature carrier.</summary>
  public static void SetCooldownSpeed(ItemStack stack, float cooldownSpeed) =>
    (stack.Attributes["temperature"] as ITreeAttribute)?.SetFloat(
      "cooldownSpeed",
      cooldownSpeed
    );

  /// <summary>Re-applies the cooldown rate to an already-stamped stack, rebasing the baseline to the
  /// stack's current temperature; call once per tick on standing molten content.</summary>
  public static void SyncCooldownSpeed(
    IWorldAccessor world,
    ItemStack stack,
    float? cooldownSpeed = null
  ) {
    SetTemperature(world, stack, GetTemperature(world, stack));
    SetCooldownSpeed(stack, cooldownSpeed ?? ExlibValues.MoltenCooldownDefault);
  }

  /// <summary>Sets the stack temperature without delaying the cooldown (the mod-wide convention).</summary>
  public static void SetTemperature(
    IWorldAccessor world,
    ItemStack stack,
    float temperature
  ) =>
    stack.Collectible.SetTemperature(
      world,
      stack,
      temperature,
      delayCooldown: false
    );

  /// <summary>Current stack temperature ( deg C).</summary>
  public static float GetTemperature(IWorldAccessor world, ItemStack stack) =>
    stack.Collectible.GetTemperature(world, stack);

  /// <summary>The stack's melting point ( deg C), resolved through a dummy slot.</summary>
  public static float MeltingPointOf(IWorldAccessor world, ItemStack stack) =>
    stack.Collectible.GetMeltingPoint(world, null, new DummySlot(stack));

  /// <summary>Classifies the stack against its melting point (liquid / cooling / hardened).</summary>
  public static MoltenState StateOf(IWorldAccessor world, ItemStack stack) =>
    Classify(
      GetTemperature(world, stack),
      MeltingPointOf(world, stack),
      stack.Collectible.Code
    );

  /// <summary>Classifies a metal at <paramref name="temperature"/> ( deg C) against its
  /// <paramref name="meltingPoint"/> using the thresholds registered for <paramref name="moltenItem"/>.</summary>
  public static MoltenState Classify(
    float temperature,
    float meltingPoint,
    AssetLocation moltenItem
  ) {
    if (
      temperature
      > MetalRegistry.LiquidThresholdOf(moltenItem) * meltingPoint
    )
      return MoltenState.Liquid;
    if (
      temperature
      < MetalRegistry.HardenedThresholdOf(moltenItem) * meltingPoint
    )
      return MoltenState.Hardened;
    return MoltenState.Cooling;
  }

  /// <summary>True when the stack has cooled below the hardened threshold (chisellable).</summary>
  public static bool IsHardened(IWorldAccessor world, ItemStack stack) =>
    StateOf(world, stack) == MoltenState.Hardened;

  /// <summary>True when the stack is hot enough to flow (above the liquid threshold).</summary>
  public static bool IsLiquid(IWorldAccessor world, ItemStack stack) =>
    StateOf(world, stack) == MoltenState.Liquid;

  /// <summary>Incandescent block-light level (0-24) for metal at <paramref name="temperature"/>.</summary>
  public static byte GlowLevel(float temperature) =>
    temperature > GlowMinTemp
      ? (byte)GameMath.Clamp((temperature - GlowMinTemp) / 30f, 0, 24)
      : (byte)0;

  /// <summary>Human-readable metal name from an item code ("game:ingot-iron" -> "Iron"); delegates to
  /// <see cref="MetalRegistry.DisplayName"/>.</summary>
  public static string DisplayName(string metalItemCode) =>
    MetalRegistry.DisplayName(metalItemCode);

  /// <summary>Formats a molten temperature for display, honouring the player's metric/imperial
  /// preference; the simulation itself stays metric.</summary>
  public static System.Func<float, string> TemperatureFormatter { get; set; } =
    t => ExMeasure.Temperature(t);

  /// <summary>"Cold" below room temperature, otherwise the formatted temperature.</summary>
  public static string FormatTemperature(float temperature) =>
    temperature < 21f
      ? Lang.Get("exlib:metalstate-cold")
      : TemperatureFormatter(temperature);

  /// <summary>Every smelted-crucible block as an <see cref="ItemStack"/>, matched by code path
  /// (<c>crucible-*-smelted</c>); scans every loaded block, call once and cache the result.</summary>
  public static ItemStack[] SmeltedCrucibleStacks(IWorldAccessor world) {
    var stacks = new List<ItemStack>();
    foreach (Block block in world.Blocks) {
      if (
        block.Code != null
        && block.Code.Path.StartsWith("crucible-")
        && block.Code.Path.EndsWith("-smelted")
      )
        stacks.Add(new ItemStack(block));
    }
    return stacks.ToArray();
  }
}
