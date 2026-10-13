#if GAME_GE_1_22
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// A block entity that crafts through its window: it lists every order it crafts, and a runner
/// lays an order's inputs with slot clicks, presses the order's button and takes its outputs.
/// </summary>
public interface ICraftStation
{
  /// <summary>Every way this station crafts, from the recipes <paramref name="world"/> has
  /// loaded.</summary>
  /// <param name="world">The world whose loaded recipes and collectibles the orders read.</param>
  /// <returns>The orders in the station's own order; empty when it crafts nothing.</returns>
  IReadOnlyList<CraftOrder> Orders(IWorldAccessor world);
}

/// <summary>One input slot of a <see cref="CraftOrder"/>.</summary>
/// <param name="Slot">The slot's index in the station's inventory, from 0.</param>
/// <param name="Name">The input's name, the key a runner's <c>with</c> picks a wildcard by: a grid
/// recipe's pattern key, or the station's own name for the slot.</param>
/// <param name="Ingredient">What the slot takes: a code or wildcard, the quantity laid, and
/// whether the stack is a tool.</param>
public sealed record CraftInput(
  int Slot,
  string Name,
  CraftingRecipeIngredient Ingredient
);

/// <summary>One way a station crafts: the inputs to lay, the button to press and the slots the
/// product comes out of.</summary>
/// <param name="Recipe">The recipe's name; null for an order no recipe backs.</param>
/// <param name="Inputs">The slots to fill, in the order they are laid.</param>
/// <param name="PressId">The packet id of the button that crafts; above 1001, the container
/// protocol's last id. Null for the hand grid, which has no button.</param>
/// <param name="PressData">The button packet's payload; null for none.</param>
/// <param name="OutputSlots">The inventory slots the product comes out of; empty for the hand
/// grid.</param>
/// <param name="Product">The stack one press makes, its stack size the quantity.</param>
/// <param name="Grid">The grid recipe behind the order, which the hand grid's matcher runs; null
/// for an order that is not a grid recipe.</param>
public sealed record CraftOrder(
  string? Recipe,
  IReadOnlyList<CraftInput> Inputs,
  int? PressId,
  byte[]? PressData,
  IReadOnlyList<int> OutputSlots,
  ItemStack Product,
  GridRecipe? Grid = null
);
#endif
