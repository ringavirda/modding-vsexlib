#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Machines;

/// <summary>What the craft step's runners share: which orders make a product, the stack a
/// slot is laid with and the stack a sequence step's hand holds.</summary>
internal static class CraftBills
{
  /// <summary>Whether <paramref name="order"/> makes <paramref name="wanted"/>, by the recipe
  /// named <paramref name="recipe"/> when that is not null.</summary>
  public static bool Makes(
    CraftOrder order,
    AssetLocation wanted,
    string? recipe
  ) =>
    order.Product.Collectible.Code.Equals(wanted)
    && (recipe == null || order.Recipe == recipe);

  /// <summary>The stack that fills <paramref name="input"/>: the collectible
  /// <paramref name="with"/> names under the input's name, else the ingredient's own item when it
  /// is exact, else the first loaded collectible the ingredient takes.</summary>
  /// <param name="world">The world whose registered items and blocks are searched.</param>
  /// <param name="input">The slot to fill.</param>
  /// <param name="with">The step's <c>with</c> picks, by input name; null for none.</param>
  /// <returns>A stack of the ingredient's quantity.</returns>
  /// <exception cref="InvalidOperationException">A pick names nothing registered or an item the
  /// slot does not take.</exception>
  public static ItemStack Laid(
    IWorldAccessor world,
    CraftInput input,
    JObject? with
  )
  {
    CraftingRecipeIngredient ingredient = input.Ingredient;
    ItemStack stack;
    if (with?[input.Name] is JValue { Value: string picked })
    {
      var code = new AssetLocation(picked);
      CollectibleObject chosen =
        (CollectibleObject?)world.GetItem(code)
        ?? world.GetBlock(code)
        ?? throw new InvalidOperationException(
          $"with {input.Name}: \"{picked}\" is not registered by the load."
        );
      stack = new ItemStack(chosen, ingredient.Quantity);
      if (!ingredient.SatisfiesAsIngredient(stack))
        throw new InvalidOperationException(
          $"with {input.Name}: {picked} is not an ingredient the slot takes."
        );
      return stack;
    }
    if (ingredient.MatchingType == EnumRecipeMatchType.Exact)
    {
      stack = ingredient.ResolvedItemStack!.Clone();
      stack.StackSize = ingredient.Quantity;
      return stack;
    }
    IEnumerable<CollectibleObject> pool =
      ingredient.Type == EnumItemClass.Item
        ? world.Items.Where(i => i?.Code != null)
        : world.Blocks.Where(b => b?.Code != null && b.Id != 0);
    return pool.Select(c => new ItemStack(c, ingredient.Quantity))
      .First(s => ingredient.SatisfiesAsIngredient(s));
  }

  /// <summary>The one-item stack a sequence step's hand holds for the tool pattern
  /// <paramref name="pattern"/>: an item the pack holds that matches it, or, with no pack, the
  /// first loaded item that does.</summary>
  /// <param name="world">The world whose registered items are searched.</param>
  /// <param name="pattern">A code with <c>*</c> wildcards, as a sequence step's <c>tool</c>.</param>
  /// <param name="pack">The case's pack, or null for none.</param>
  /// <param name="drawn">The pack key the stack stands for, which the caller draws; null with no
  /// pack.</param>
  /// <returns>A stack of one at full durability.</returns>
  /// <exception cref="InvalidOperationException">The pack holds nothing that matches, or, with no
  /// pack, nothing loaded does.</exception>
  public static ItemStack Tool(
    IWorldAccessor world,
    string pattern,
    SetupPack? pack,
    out string? drawn
  )
  {
    drawn = null;
    if (pack == null)
      return new ItemStack(
        world.Items.FirstOrDefault(i =>
          i?.Code != null && WildcardUtil.Match(pattern, i.Code.ToString())
        ) ?? throw new InvalidOperationException($"no loaded item matches {pattern}."),
        1
      );
    drawn =
      pack.HeldKeys.FirstOrDefault(k => WildcardUtil.Match(pattern, k))
      ?? throw new InvalidOperationException($"the pack holds no {pattern}.");
    var code = new AssetLocation(drawn);
    return new ItemStack(
      (CollectibleObject?)world.GetItem(code)
        ?? world.GetBlock(code)
        ?? throw new InvalidOperationException(
          $"the pack holds {drawn}, which the load did not register."
        ),
      1
    );
  }
}
#endif
