// Shims that re-expose current-version VintageStory API members on the older game APIs the mods
// can also target. Guarded by !GAME_GE_1_22 (game version below 1.22).
#if !GAME_GE_1_22
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Legacy;

/// <summary>C# 14 extension members that fill in API members missing from the pre-1.22 game surface.</summary>
public static class LegacyApi {
  extension(GridRecipe recipe) {
    /// <summary>The 1.22 property name for the <c>resolvedIngredients</c> field.</summary>
    public CraftingRecipeIngredient[] ResolvedIngredients =>
      recipe.resolvedIngredients;

    /// <summary>The 1.22 overload, which takes the world explicitly.</summary>
    public bool Matches(
      IPlayer forPlayer,
      IWorldAccessor world,
      ItemSlot[] ingredients,
      int gridWidth
    ) => recipe.Matches(forPlayer, ingredients, gridWidth);
  }

  extension(CraftingRecipeIngredient ingredient) {
    /// <summary>The 1.22 casing of <c>ResolvedItemstack</c>.</summary>
    public ItemStack ResolvedItemStack {
      get => ingredient.ResolvedItemstack;
      set => ingredient.ResolvedItemstack = value;
    }
  }

  extension(EvolvingNatFloat? evolve) {
    /// <summary>Mirrors the <see cref="System.Nullable{T}"/> surface the 1.22 struct form exposes.</summary>
    public bool HasValue => evolve != null;

    /// <summary>The non-null value, matching Nullable's accessor on 1.22.</summary>
    public EvolvingNatFloat Value => evolve!;
  }

  extension(MultiblockStructure structure) {
    /// <summary>The transformed-offsets list, valid only after <c>InitForUse()</c> has run.</summary>
    public List<BlockOffsetAndNumber>? TransformedOffsets =>
      (List<BlockOffsetAndNumber>?)TransformedOffsetsField.GetValue(structure);
  }

  private static readonly FieldInfo TransformedOffsetsField =
    typeof(MultiblockStructure).GetField(
      "TransformedOffsets",
      BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
    )!;
}
#endif
