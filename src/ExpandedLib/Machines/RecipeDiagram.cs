#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// The diagram a grid recipe is keyed to, in <c>attributes.diagram</c> as the diagram's full code,
/// and <c>attributes.consumeDiagram</c>, true when each craft spends one. A keyed recipe crafts only
/// under <see cref="CraftSequenceGate.At(string, string?)"/> marked with that diagram.
/// </summary>
public static class RecipeDiagram
{
  /// <summary>The widest or tallest grid, in cells, that the hand grid can lay.</summary>
  private const int HandGridSize = 3;

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

  /// <summary>Hides from the handbook's Created by every grid recipe a station makes: one with a diagram
  /// key, one with a craft sequence in <see cref="CraftSequenceRegistry.Shared"/>, or one wider or
  /// taller than the hand grid. Run on the server after the sequences load; the flag crosses the
  /// recipe packet to every client.</summary>
  /// <param name="world">The world whose <c>GridRecipes</c> are changed.</param>
  /// <returns>The number of recipes whose flag this call cleared.</returns>
  public static int HideStationRecipes(IWorldAccessor world)
  {
    int hidden = 0;
    foreach (GridRecipe recipe in world.GridRecipes)
    {
      if (
        !recipe.ShowInCreatedBy
        || (
          Of(recipe) == null
          && CraftSequenceRegistry.Shared.Sequence(recipe.Name?.ToString())
            == null
          && recipe.Width <= HandGridSize
          && recipe.Height <= HandGridSize
        )
      )
        continue;
      recipe.ShowInCreatedBy = false;
      hidden++;
    }
    return hidden;
  }

  /// <summary>The grid recipes keyed to <paramref name="diagram"/>.</summary>
  /// <param name="world">The world whose <c>GridRecipes</c> are read.</param>
  /// <param name="diagram">The diagram's full code, compared ordinally.</param>
  /// <returns>The recipes in load order; empty when none is keyed to it.</returns>
  public static IReadOnlyList<GridRecipe> Makes(
    IWorldAccessor world,
    string diagram
  ) =>
    world
      .GridRecipes.Where(r =>
        string.Equals(Of(r), diagram, StringComparison.Ordinal)
      )
      .ToList();

  /// <summary>The diagram recipes that make <paramref name="product"/>, each with the station that
  /// holds the diagram slot.</summary>
  /// <param name="world">The world whose recipes, blocks and items are read.</param>
  /// <param name="product">The collectible a recipe's output must be.</param>
  /// <returns>One use per keyed recipe whose output is <paramref name="product"/> and whose diagram is
  /// a loaded item, in load order; empty when no loaded block has <see cref="StationAttribute"/> true.
  /// The station is the first such block.</returns>
  public static IReadOnlyList<DiagramUse> MadeAt(
    IWorldAccessor world,
    CollectibleObject product
  )
  {
    Block? station = world.Blocks.FirstOrDefault(b =>
      b?.Attributes?[StationAttribute].AsBool(false) ?? false
    );
    if (station == null)
      return [];
    var uses = new List<DiagramUse>();
    foreach (GridRecipe recipe in world.GridRecipes)
    {
      if (Of(recipe) is not { } code)
        continue;
      Item? diagram = world.GetItem(new AssetLocation(code));
      CollectibleObject? made = recipe.Output?.ResolvedItemStack?.Collectible;
      if (diagram != null && made != null && made.Code.Equals(product.Code))
        uses.Add(new DiagramUse(station, diagram, recipe));
    }
    return uses;
  }
}

/// <summary>A diagram recipe that makes a product: the station it is worked at, the diagram it
/// needs, and the recipe.</summary>
/// <param name="Station">The block that holds the diagram slot.</param>
/// <param name="Diagram">The diagram item the recipe is keyed to.</param>
/// <param name="Recipe">The keyed grid recipe.</param>
public sealed record DiagramUse(Block Station, Item Diagram, GridRecipe Recipe);
#endif
