#if GAME_GE_1_22
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>The player's 3 x 3 crafting grid as a craft station's orders.</summary>
public static class HandGrid
{
  /// <summary>The grid's width and height in slots.</summary>
  public const int Size = 3;

  /// <summary>Every loaded grid recipe the hand grid can hold, each laid at the grid's top left.
  /// A recipe wider or taller than <see cref="Size"/>, or whose output did not resolve to a stack, is
  /// left out.</summary>
  /// <param name="world">The world whose <c>GridRecipes</c> are read.</param>
  /// <returns>One order per recipe, in load order; the slots are row-major over the 3 x 3 grid.
  /// A shapeless recipe's inputs fill slots from 0.</returns>
  public static IReadOnlyList<CraftOrder> Orders(IWorldAccessor world)
  {
    var orders = new List<CraftOrder>();
    foreach (GridRecipe recipe in world.GridRecipes)
    {
      if (
        recipe.Width > Size
        || recipe.Height > Size
        || recipe.Output?.ResolvedItemStack == null
      )
        continue;
      var inputs = new List<CraftInput>();
      for (int i = 0; i < recipe.ResolvedIngredients!.Length; i++)
      {
        if (recipe.ResolvedIngredients[i] is not { } ingredient)
          continue;
        int slot = recipe.Shapeless
          ? inputs.Count
          : (i / recipe.Width * Size) + (i % recipe.Width);
        string name = string.IsNullOrEmpty(ingredient.Id)
          ? ingredient.Name ?? ""
          : ingredient.Id;
        inputs.Add(new CraftInput(slot, name, ingredient));
      }
      orders.Add(
        new CraftOrder(
          recipe.Name?.ToString(),
          inputs,
          null,
          null,
          [],
          recipe.Output.ResolvedItemStack.Clone(),
          recipe
        )
      );
    }
    return orders;
  }
}
#endif
