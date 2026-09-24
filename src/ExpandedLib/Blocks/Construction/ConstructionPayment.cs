using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Blocks;

/// <summary>The one-material rule of a construction stage: every ingredient that stores a wildcard
/// key is paid in one variant of it.</summary>
internal static class ConstructionPayment {
  /// <summary>The first stored key <paramref name="hotbar"/> would be paid in two variants, walking
  /// the non-empty slots in order and taking from each slot an ingredient accepts until its
  /// quantity is met, as the game's construction does.</summary>
  /// <param name="ingredients">The stage's ingredients, placeholders filled and their matching
  /// type resolved, each with the key it stores or null.</param>
  /// <param name="hotbar">The slots the stage is paid from.</param>
  /// <returns>The key and its two variants in the order taken, or null when no key is paid in two
  /// variants.</returns>
  internal static (string Key, string First, string Second)? MixedVariant(
    IEnumerable<(CraftingRecipeIngredient Ingredient, string? Key)> ingredients,
    IEnumerable<ItemSlot> hotbar
  ) {
    List<ItemSlot> slots = [.. hotbar.Where(s => !s.Empty)];
    var taken = new Dictionary<string, string>();
    foreach ((CraftingRecipeIngredient ingredient, string? key) in ingredients) {
      if (key == null)
        continue;
      int left = ingredient.Quantity;
      foreach (ItemSlot slot in slots) {
        if (left <= 0)
          break;
        if (!ingredient.SatisfiesAsIngredient(slot.Itemstack, false))
          continue;
        left -= slot.Itemstack.StackSize;
        if (
          !slot.Itemstack.Collectible.Variant.TryGetValue(key, out var variant)
          || variant == null
        )
          continue;
        if (!taken.TryGetValue(key, out string? first))
          taken[key] = variant;
        else if (first != variant)
          return (key, first, variant);
      }
    }
    return null;
  }

  /// <summary>Tells the player on <paramref name="api"/>'s client why the stage was refused;
  /// nothing on the server.</summary>
  internal static void Refuse(
    ICoreAPI api,
    object sender,
    (string Key, string First, string Second) mixed
  ) {
    if (api is ICoreClientAPI capi)
      capi.TriggerIngameError(
        sender,
        "onematerial",
        Lang.Get(
          "exlib:ingameerror-construction-onematerial",
          MaterialName(mixed.First),
          MaterialName(mixed.Second)
        )
      );
  }

  private static string MaterialName(string variant) {
    string key = "game:material-" + variant;
    return Lang.Get(key);
  }
}
