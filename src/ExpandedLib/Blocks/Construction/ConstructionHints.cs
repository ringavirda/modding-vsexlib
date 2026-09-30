using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks;

/// <summary>What decides which collectibles satisfy a filled ingredient. Quantity is not part of
/// it: the hint sizes each shown stack itself.</summary>
internal readonly record struct HintKey(
  EnumItemClass Type,
  string? Code,
  bool MatchesByPattern,
  string? AllowedVariants,
  string? SkipVariants,
  string? Attributes
#if GAME_GE_1_22
  ,
  ComplexTagCondition<TagSet> Tags
#endif
) {
  public static HintKey Of(ExConstructionIngredient ingredient) =>
    new(
      ingredient.Type,
      ingredient.Code?.ToString(),
      ingredient.MatchesByPattern,
      ingredient.AllowedVariants == null
        ? null
        : string.Join('\n', ingredient.AllowedVariants),
      ingredient.SkipVariants == null
        ? null
        : string.Join('\n', ingredient.SkipVariants),
      ingredient.Attributes?.Token?.ToString(Formatting.None)
#if GAME_GE_1_22
      ,
      ingredient.Tags
#endif
    );
}

/// <summary>The collectibles that satisfy an ingredient, scanned once per world for each distinct
/// <see cref="HintKey"/>. A world keeps its entries only while it lives. Used from a world's main
/// thread only.</summary>
internal static class ConstructionHints {
  private static readonly ConditionalWeakTable<
    IWorldAccessor,
    Dictionary<HintKey, CollectibleObject[]>
  > Worlds = new();

  /// <summary>The collectibles of <paramref name="world"/> that satisfy the filled, resolved
  /// <paramref name="ingredient"/>. The array is shared; callers do not mutate it.</summary>
  public static CollectibleObject[] Matching(
    IWorldAccessor world,
    ExConstructionIngredient ingredient
  ) {
    var entries = Worlds.GetValue(world, _ => new());
    var key = HintKey.Of(ingredient);
    if (entries.TryGetValue(key, out var cached))
      return cached;

    var matching = new List<CollectibleObject>();
    foreach (var collectible in world.Collectibles)
      if (
        collectible != null
        && ingredient.SatisfiesAsIngredient(
          new ItemStack(collectible, 1),
          false
        )
      )
        matching.Add(collectible);
    return entries[key] = matching.ToArray();
  }
}
