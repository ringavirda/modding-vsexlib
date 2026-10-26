#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

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

  /// <summary>What <paramref name="times"/> crafts of <paramref name="order"/> make: its product and
  /// the byproducts of <paramref name="sequence"/>, each code counted once.</summary>
  /// <param name="order">The order crafted.</param>
  /// <param name="sequence">The order's craft sequence, or null for none.</param>
  /// <param name="times">How many crafts; at least 1.</param>
  /// <returns>The count of each collectible code, the product first.</returns>
  public static Dictionary<string, int> Yield(
    CraftOrder order,
    CraftSequence? sequence,
    int times
  )
  {
    var wanted = new Dictionary<string, int>
    {
      [order.Product.Collectible.Code.ToString()] =
        order.Product.StackSize * times,
    };
    foreach (CraftByproduct by in sequence?.Byproducts ?? [])
    {
      string code = new AssetLocation(by.Code).ToString();
      wanted[code] = wanted.GetValueOrDefault(code) + (by.Quantity * times);
    }
    return wanted;
  }

  /// <summary>Whether <paramref name="made"/> holds exactly the codes and counts of
  /// <paramref name="wanted"/>.</summary>
  /// <param name="made">The stacks a craft took out.</param>
  /// <param name="wanted">What <see cref="Yield"/> answered.</param>
  public static bool Yielded(
    IEnumerable<ItemStack> made,
    IReadOnlyDictionary<string, int> wanted
  )
  {
    Dictionary<string, int> got = made.GroupBy(s =>
        s.Collectible.Code.ToString()
      )
      .ToDictionary(g => g.Key, g => g.Sum(s => s.StackSize));
    return got.Count == wanted.Count
      && wanted.All(w => got.GetValueOrDefault(w.Key) == w.Value);
  }

  /// <summary>The liquid a grid recipe's bill takes from its vessel: the recipe's
  /// <c>liquidContainerProps</c> attribute.</summary>
  /// <param name="Content">The liquid item's code.</param>
  /// <param name="Litres">The litres one craft drains; above 0.</param>
  public sealed record LiquidBill(AssetLocation Content, float Litres);

  /// <summary>The liquid <paramref name="recipe"/> takes from a vessel in its bill.</summary>
  /// <param name="recipe">The grid recipe behind an order; null for none.</param>
  /// <returns>The liquid and litres, or null when the recipe carries no <c>liquidContainerProps</c>
  /// naming a <c>requiresContent</c> code.</returns>
  public static LiquidBill? Liquid(GridRecipe? recipe)
  {
    JsonObject? props = recipe?.Attributes?["liquidContainerProps"];
    return
      props?.Exists == true
      && props["requiresContent"]["code"].AsString() is { } code
      ? new LiquidBill(
        new AssetLocation(code),
        props["requiresLitres"].AsFloat()
      )
      : null;
  }

  private static bool Vessel(CraftOrder order, ItemStack? stack) =>
    stack?.Collectible is BlockLiquidContainerBase
    && Liquid(order.Grid) != null;

  /// <summary>Fills a vessel laid for a bill that pays a liquid with the litres the bill asks for; any
  /// other stack is returned as it is.</summary>
  /// <param name="world">The world that resolves the liquid item.</param>
  /// <param name="order">The order whose bill is laid.</param>
  /// <param name="laid">The stack laid for one of the order's inputs.</param>
  /// <returns><paramref name="laid"/>.</returns>
  /// <exception cref="InvalidOperationException">The vessel refuses the liquid or takes less than
  /// the litres.</exception>
  public static ItemStack Filled(
    IWorldAccessor world,
    CraftOrder order,
    ItemStack laid
  )
  {
    if (Vessel(order, laid) && Liquid(order.Grid) is { } bill)
      if (
        HeldContent.Fill(world, laid, bill.Content.ToString(), bill.Litres) is
        { } refusal
      )
        throw new InvalidOperationException(refusal);
    return laid;
  }

  /// <summary>Whether <paramref name="stack"/> pays <paramref name="input"/> of <paramref name="order"/>:
  /// the ingredient takes it in the quantity asked, and a vessel under a liquid bill holds its liquid
  /// in at least the litres.</summary>
  public static bool Pays(
    CraftOrder order,
    CraftInput input,
    ItemStack stack
  ) =>
    stack.StackSize >= input.Ingredient.Quantity
    && input.Ingredient.SatisfiesAsIngredient(stack)
    && (
      !Vessel(order, stack)
      || stack.Collectible.MatchesForCrafting(
        stack,
        order.Grid,
        input.Ingredient
      )
    );

  /// <summary>Takes the bill's share of <paramref name="input"/> out of <paramref name="slot"/>: the
  /// ingredient's quantity of stack, or, for a vessel under a liquid bill, the litres, the vessel
  /// staying in the slot.</summary>
  public static void Take(CraftOrder order, CraftInput input, ItemSlot slot)
  {
    if (Vessel(order, slot.Itemstack))
      slot.Itemstack.Collectible.OnConsumedByCrafting(
        [slot],
        slot,
        order.Grid,
        input.Ingredient,
        null!,
        input.Ingredient.Quantity
      );
    else
      slot.TakeOut(input.Ingredient.Quantity);
    slot.MarkDirty();
  }

  /// <summary>How long a runner holds <paramref name="step"/> with <paramref name="tool"/> in hand.</summary>
  /// <param name="step">The step held.</param>
  /// <param name="tool">The stack in the active hand; null for bare hands.</param>
  /// <returns>Seconds: the step's own, or its hits as swings of the tool's stroke.</returns>
  public static float Duration(CraftStep step, ItemStack? tool) =>
    step.Duration(
      CraftSequenceRegistry
        .Shared.ToolDefault(tool?.Collectible.Code.ToString())
        ?.Stroke
        ?? CraftToolDefault.DefaultStroke
    );

  /// <summary>What <paramref name="slots"/> hold now, for <see cref="HandedOver"/> to compare against
  /// after a press.</summary>
  /// <param name="slots">The player's slots: the hotbar, the active slot and the backpack.</param>
  /// <returns>Each slot's collectible and stack size; the collectible is null for an empty slot.</returns>
  public static Dictionary<
    ItemSlot,
    (CollectibleObject? Collectible, int Size)
  > Carried(IEnumerable<ItemSlot> slots) =>
    slots
      .Distinct()
      .ToDictionary(s => s, s => (s.Itemstack?.Collectible, s.StackSize));

  /// <summary>Takes out of <paramref name="slots"/> what a station handed the player since
  /// <paramref name="before"/>, so a product that does not wait in an output slot is counted as made.
  /// Each slot's growth leaves the slot; a slot that holds another collectible than before counts whole.</summary>
  /// <param name="before">What <see cref="Carried"/> answered before the press.</param>
  /// <param name="slots">The same slots.</param>
  /// <returns>The stacks handed over, one per grown slot; empty when nothing grew.</returns>
  public static List<ItemStack> HandedOver(
    Dictionary<ItemSlot, (CollectibleObject? Collectible, int Size)> before,
    IEnumerable<ItemSlot> slots
  )
  {
    var got = new List<ItemStack>();
    foreach (ItemSlot slot in slots.Distinct())
    {
      if (slot.Empty)
        continue;
      (CollectibleObject? was, int size) = before.GetValueOrDefault(slot);
      int grew =
        slot.StackSize - (was == slot.Itemstack.Collectible ? size : 0);
      if (grew <= 0)
        continue;
      ItemStack stack = slot.Itemstack.Clone();
      stack.StackSize = grew;
      got.Add(stack);
      slot.Itemstack.StackSize -= grew;
      if (slot.Itemstack.StackSize <= 0)
        slot.Itemstack = null;
    }
    return got;
  }

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
        )
          ?? throw new InvalidOperationException(
            $"no loaded item matches {pattern}."
          ),
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
