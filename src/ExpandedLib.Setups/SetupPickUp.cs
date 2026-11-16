using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Scenarios;

/// <summary>What the <c>pickup</c> step chooses, takes and refuses, shared by the harness and the live runner.
/// Positions are world coordinates.</summary>
internal static class SetupPickUp {
  /// <summary>Chooses the first candidate within one cell of <paramref name="cell"/> on each axis whose stack is
  /// not empty and whose code matches <paramref name="code"/>.</summary>
  /// <param name="candidates">The item entities in the order they spawned, with their world positions.</param>
  /// <param name="cell">The cell the step names.</param>
  /// <param name="code">An asset location, domain <c>game</c> by default, <c>*</c> matching any run of
  /// characters; null matches every stack.</param>
  /// <param name="where">The cell as the runner writes it in a refusal.</param>
  /// <param name="problem">Why nothing was chosen, null when something was.</param>
  /// <returns>The index into <paramref name="candidates"/>, or null when no candidate qualifies.</returns>
  public static int? Pick(
    IReadOnlyList<(ItemStack Stack, Vec3d At)> candidates,
    BlockPos cell,
    string? code,
    string where,
    out string? problem
  ) {
    var near = new List<int>();
    for (int i = 0; i < candidates.Count; i++) {
      var (stack, at) = candidates[i];
      if (
        stack.StackSize >= 1
        && Math.Abs(Math.Floor(at.X) - cell.X) <= 1
        && Math.Abs(Math.Floor(at.Y) - cell.Y) <= 1
        && Math.Abs(Math.Floor(at.Z) - cell.Z) <= 1
      )
        near.Add(i);
    }
    if (near.Count == 0) {
      problem = $"no item entity lies within one cell of {where}.";
      return null;
    }
    if (code == null) {
      problem = null;
      return near[0];
    }
    var wanted = new AssetLocation(code);
    foreach (int i in near)
      if (WildcardUtil.Match(wanted, candidates[i].Stack.Collectible.Code)) {
        problem = null;
        return i;
      }
    string there = string.Join(
      ", ",
      near.Select(i =>
        $"{candidates[i].Stack.Collectible.Code} x{candidates[i].Stack.StackSize}"
      )
    );
    problem =
      $"no item entity within one cell of {where} is {code}; there: {there}.";
    return null;
  }

  /// <summary>The stack the hand receives for a spawned one.</summary>
  /// <returns>A clone of <paramref name="spawned"/>, whole stack with its attribute tree.</returns>
  public static ItemStack Taken(ItemStack spawned) => spawned.Clone();

  /// <summary>Why the hand cannot receive a pick.</summary>
  /// <param name="held">The active hotbar slot's stack, null when it is empty.</param>
  /// <returns>The refusal text, or null when the slot is empty.</returns>
  public static string? HandRefusal(ItemStack? held) =>
    held == null || held.StackSize < 1
      ? null
      : $"the active hotbar slot holds {held.Collectible.Code} x{held.StackSize}; \"pickup\" takes into an empty hand.";

  /// <summary>Why a pick is too hot to hold: above <c>GlobalConstants.TooHotToTouchTemperature</c>, its
  /// collectible not <c>allowHotCrafting</c>, and the off hand not <c>heatResistant</c>.</summary>
  /// <param name="world">The world whose calendar the stack's temperature is read against.</param>
  /// <param name="picked">The stack taken.</param>
  /// <param name="offHand">The stack in the off hand, null when it is empty.</param>
  /// <returns>The refusal text, or null when the stack may be held.</returns>
  public static string? HeatRefusal(
    IWorldAccessor world,
    ItemStack picked,
    ItemStack? offHand
  ) {
    if (picked.Collectible.Attributes?.IsTrue("allowHotCrafting") == true)
      return null;
    float t = picked.Collectible.GetTemperature(world, picked);
    if (!(t > GlobalConstants.TooHotToTouchTemperature))
      return null;
    if (offHand?.Collectible.Attributes?.IsTrue("heatResistant") == true)
      return null;
    return $"{picked.Collectible.Code} x{picked.StackSize} is {Math.Round(t).ToString(CultureInfo.InvariantCulture)} C, too hot to hold; \"pickup\" needs a heatResistant offHand, such as tongs.";
  }
}
