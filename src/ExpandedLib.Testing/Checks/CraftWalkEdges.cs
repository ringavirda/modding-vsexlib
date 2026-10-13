#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Machines;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>Where a recipe is crafted.</summary>
internal enum CraftWalkClass
{
  /// <summary>The 3 x 3 hand grid.</summary>
  HandGrid,

  /// <summary>A station whose orders hold the recipe: wider or taller than the hand grid.</summary>
  GridStation,

  /// <summary>Smithing, clay forming, a process or a construction: followed, not crafted.</summary>
  Followed,
}

/// <summary>One thing an edge asks for: the loaded codes that satisfy it.</summary>
/// <param name="Label">The ingredient's code as the recipe writes it.</param>
/// <param name="Codes">Every loaded code that satisfies it.</param>
internal sealed record WalkNeed(string Label, IReadOnlyList<string> Codes);

/// <summary>One recipe of the walk's graph.</summary>
/// <param name="Kind">The registry: <c>grid</c>, <c>smithing</c> or <c>clay forming</c>.</param>
/// <param name="Name">The recipe's name, or its output when it has none.</param>
/// <param name="Class">Where the recipe is crafted.</param>
/// <param name="Needs">What the recipe asks for, one entry for each distinct ingredient.</param>
/// <param name="Output">The code the recipe makes.</param>
/// <param name="OnTrust">Whether an ingredient carries attributes the walk takes on trust.</param>
/// <param name="Grid">The grid recipe behind a grid edge; null for the others.</param>
internal sealed record WalkEdge(
  string Kind,
  string Name,
  CraftWalkClass Class,
  IReadOnlyList<WalkNeed> Needs,
  string Output,
  bool OnTrust,
  GridRecipe? Grid = null
);

internal static class CraftWalkEdges
{
  private static readonly char[] Wild = ['*', '@', '{', '('];

  /// <summary>Where <paramref name="recipe"/> is crafted: the hand grid when it fits 3 x 3, else
  /// a grid station.</summary>
  internal static CraftWalkClass ClassOf(GridRecipe recipe) =>
    recipe.Width > HandGrid.Size || recipe.Height > HandGrid.Size
      ? CraftWalkClass.GridStation
      : CraftWalkClass.HandGrid;

  /// <summary>The edges of every grid, smithing and clay forming recipe the world loaded whose
  /// output is in <paramref name="domain"/>.</summary>
  internal static List<WalkEdge> Read(
    TestWorld world,
    string domain,
    IReadOnlyList<CollectibleObject> all
  )
  {
    var cache = new Dictionary<string, WalkNeed>(StringComparer.Ordinal);
    var edges = new List<WalkEdge>();
    foreach (GridRecipe recipe in world.World.GridRecipes)
      if (
        recipe.Output?.ResolvedItemStack?.Collectible.Code is { } output
        && output.Domain == domain
      )
        edges.Add(
          Edge(
            "grid",
            recipe,
            recipe.ResolvedIngredients ?? [],
            output,
            ClassOf(recipe),
            all,
            cache,
            recipe
          )
        );
    foreach (SmithingRecipe recipe in world.SmithingRecipes)
      if (OutputOf(recipe.Output) is { } output && output.Domain == domain)
        edges.Add(
          Edge(
            "smithing",
            recipe,
            recipe.Ingredients ?? [],
            output,
            CraftWalkClass.Followed,
            all,
            cache,
            null
          )
        );
    foreach (ClayFormingRecipe recipe in world.ClayFormingRecipes)
      if (OutputOf(recipe.Output) is { } output && output.Domain == domain)
        edges.Add(
          Edge(
            "clay forming",
            recipe,
            recipe.Ingredients ?? [],
            output,
            CraftWalkClass.Followed,
            all,
            cache,
            null
          )
        );
    return edges;
  }

  private static AssetLocation? OutputOf(JsonItemStack? stack) =>
    stack?.ResolvedItemStack?.Collectible.Code ?? stack?.Code;

  private static WalkEdge Edge(
    string kind,
    IRecipeBase recipe,
    IEnumerable<CraftingRecipeIngredient?> ingredients,
    AssetLocation output,
    CraftWalkClass at,
    IReadOnlyList<CollectibleObject> all,
    Dictionary<string, WalkNeed> cache,
    GridRecipe? grid
  )
  {
    CraftingRecipeIngredient[] asked =
    [
      .. ingredients.OfType<CraftingRecipeIngredient>(),
    ];
    return new WalkEdge(
      kind,
      recipe.Name?.ToString() ?? output.ToString(),
      at,
      [
        .. asked
          .Select(i => Need(i, all, cache))
          .DistinctBy(n => n.Label + "|" + string.Join(",", n.Codes.Take(3))),
      ],
      output.ToString(),
      asked.Any(i => i.Attributes is { Exists: true }),
      grid
    );
  }

  private static WalkNeed Need(
    CraftingRecipeIngredient ingredient,
    IReadOnlyList<CollectibleObject> all,
    Dictionary<string, WalkNeed> cache
  )
  {
    string label =
      (
        ingredient.MatchingType == EnumRecipeMatchType.Exact
          ? ingredient.ResolvedItemStack?.Collectible.Code ?? ingredient.Code
          : ingredient.Code
      )?.ToString() ?? "?";
    string key =
      $"{ingredient.Type}|{ingredient.MatchingType}|{label}|{string.Join(",", ingredient.AllowedVariants ?? [])}|{string.Join(",", ingredient.SkipVariants ?? [])}|{ingredient.Tags}";
    if (cache.TryGetValue(key, out WalkNeed? known))
      return known;
    string[] codes;
    if (ingredient.MatchingType == EnumRecipeMatchType.Exact)
      codes = ingredient.ResolvedItemStack is { } stack
        ? [stack.Collectible.Code.ToString()]
        : [];
    else
    {
      string path = ingredient.Code?.Path ?? "*";
      int wild = path.IndexOfAny(Wild);
      string prefix = wild < 0 ? path : path[..wild];
      codes =
      [
        .. all.Where(c =>
            c.ItemClass == ingredient.Type
            && c.Code.Path.StartsWith(prefix, StringComparison.Ordinal)
            && ingredient.SatisfiesAsIngredient(new ItemStack(c), false)
          )
          .Select(c => c.Code.ToString()),
      ];
    }
    return cache[key] = new WalkNeed(label, codes);
  }
}
#endif
