#if GAME_GE_1_22
using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// The diagram a grid recipe is keyed to, in <c>attributes.diagram</c> as the diagram's full code,
/// and <c>attributes.consumeDiagram</c>, true when each craft spends one. A keyed recipe crafts only
/// under <see cref="CraftSequenceGate.At(string, string?)"/> marked with that diagram.
/// </summary>
public static class RecipeDiagram
{
  /// <summary>The recipe attribute holding the diagram's full code.</summary>
  public const string DiagramAttribute = "diagram";

  /// <summary>The recipe attribute that is true when each craft spends one diagram.</summary>
  public const string ConsumeAttribute = "consumeDiagram";

  /// <summary>The block attribute that marks a block as a station with a diagram slot.</summary>
  public const string StationAttribute = "diagramStation";

  /// <summary>The diagram <paramref name="recipe"/> is keyed to.</summary>
  /// <param name="recipe">The grid recipe.</param>
  /// <returns>The diagram's full code, or null when the recipe has no key or an empty one.</returns>
  public static string? Of(GridRecipe recipe)
  {
    string? code = recipe.Attributes?[DiagramAttribute].AsString();
    return string.IsNullOrEmpty(code) ? null : code;
  }

  /// <summary>Whether each craft of <paramref name="recipe"/> spends one diagram.</summary>
  /// <param name="recipe">The grid recipe.</param>
  /// <returns>True when <c>attributes.consumeDiagram</c> is true; false when it is absent or false,
  /// and for a recipe with no diagram.</returns>
  public static bool Consumes(GridRecipe recipe) =>
    Of(recipe) != null
    && (recipe.Attributes?[ConsumeAttribute].AsBool(false) ?? false);

  /// <summary>Whether <paramref name="collectible"/> is a diagram: its code path starts
  /// <c>diagram-</c>, in any domain.</summary>
  /// <param name="collectible">The item or block; may be null.</param>
  /// <returns>False for null.</returns>
  public static bool IsDiagram(CollectibleObject? collectible) =>
    collectible?.Code?.Path.StartsWith("diagram-", StringComparison.Ordinal)
    ?? false;

  /// <summary>The diagram <paramref name="recipe"/> asks for, as an ingredient.</summary>
  /// <param name="recipe">The grid recipe.</param>
  /// <param name="world">The world whose items the diagram's code is looked up in.</param>
  /// <returns>One resolved diagram, a tool unless the recipe <see cref="Consumes"/> it; null when the
  /// recipe has no key or the key's code is not a loaded item.</returns>
  public static CraftingRecipeIngredient? Ingredient(
    GridRecipe recipe,
    IWorldAccessor world
  )
  {
    if (Of(recipe) is not { } code)
      return null;
    var location = new AssetLocation(code);
    if (world.GetItem(location) == null)
      return null;
    var ingredient = new CraftingRecipeIngredient
    {
      Type = EnumItemClass.Item,
      Code = location,
      Quantity = 1,
      IsTool = !Consumes(recipe),
    };
    return ingredient.Resolve(world, nameof(RecipeDiagram)) ? ingredient : null;
  }
}
#endif
