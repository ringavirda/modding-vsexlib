using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Scenarios;

/// <summary>What a <c>complete</c> step's <c>to</c> and <c>materials</c> mean, read the same way by
/// the fake world and the live runner.</summary>
internal static class SetupConstruction {
  /// <summary>The last stage index <paramref name="to"/> asks for.</summary>
  /// <param name="to">A stage index, or "all", true or null for the last.</param>
  /// <param name="stages">The number of stages the construction has.</param>
  /// <exception cref="SetupFileException">The stage is not an index of the construction.</exception>
  public static int Target(JToken? to, int stages) {
    if (
      to == null
      || to.Type == JTokenType.Boolean && (bool)to
      || to.Type == JTokenType.String && (string?)to == "all"
    )
      return stages - 1;
    if (to.Type != JTokenType.Integer || (int)to < 0 || (int)to >= stages)
      throw new SetupFileException(
        $"\"to\" {to} is not a stage of a construction of {stages} stage(s), 0 to {stages - 1}."
      );
    return (int)to;
  }

  /// <summary>Fails a stage index asked of a block that has no construction.</summary>
  /// <param name="to">The step's <c>to</c>, or null.</param>
  /// <param name="has">Whether the block carries a construction.</param>
  /// <param name="block">The block and cell, as the message names them.</param>
  /// <exception cref="SetupFileException"><paramref name="to"/> is an integer and
  /// <paramref name="has"/> is false.</exception>
  public static void RequireConstruction(JToken? to, bool has, string block) {
    if (to is { Type: JTokenType.Integer } && !has)
      throw new SetupFileException(
        $"\"to\" {to} needs a construction, and {block} carries none."
      );
  }

  /// <summary>The variant each stored wildcard key is supplied in.</summary>
  /// <exception cref="SetupFileException">The token is not an object of strings.</exception>
  public static Dictionary<string, string> Materials(JToken? token) {
    var materials = new Dictionary<string, string>();
    if (token == null)
      return materials;
    if (
      token is not JObject o
      || o.Properties().Any(p => p.Value.Type != JTokenType.String)
    )
      throw new SetupFileException(
        "\"materials\" maps each wildcard key to a variant name."
      );
    foreach (JProperty p in o.Properties())
      materials[p.Name] = (string)p.Value!;
    return materials;
  }

  /// <summary>A stage requirement's code as the pack is drawn for it: each <c>{key}</c> read as the
  /// variant <paramref name="materials"/> names for the key, or <c>*</c> when it names none.</summary>
  public static string DrawnCode(
    string code,
    IReadOnlyDictionary<string, string> materials
  ) =>
    Regex.Replace(
      code,
      @"\{([^}]*)\}",
      m => materials.GetValueOrDefault(m.Groups[1].Value, "*")
    );

  /// <summary>Whether a collectible whose variant under <paramref name="key"/> is
  /// <paramref name="variant"/> can supply an ingredient that stores <paramref name="key"/>.</summary>
  /// <param name="key">The key the ingredient stores, or null for an ingredient that stores none.</param>
  /// <param name="wanted">The variant each key must be supplied in; a key it lacks takes any.</param>
  public static bool Allows(
    string? key,
    string? variant,
    IReadOnlyDictionary<string, string> wanted
  ) =>
    key == null
    || !wanted.TryGetValue(key, out string? only)
    || only == variant;

  /// <summary>The stacks a click pays <paramref name="stage"/> with: for each ingredient the first
  /// registered collectible that satisfies it, in the variant <paramref name="materials"/> names for a
  /// stored key and the one an earlier ingredient of the stage took for the same key.</summary>
  /// <param name="world">The world whose registered items and blocks are searched.</param>
  /// <param name="rcc">The construction; its stored wildcards fill the ingredients' placeholders.</param>
  /// <param name="stage">The stage index, 0 to the construction's stage count - 1.</param>
  /// <param name="materials">The variant each stored wildcard key is supplied in.</param>
  /// <returns>One stack per ingredient, in the stage's order, each of the ingredient's quantity.</returns>
  /// <exception cref="SetupFileException">An ingredient is satisfied by no registered collectible.</exception>
  public static List<ItemStack> Supply(
    IWorldAccessor world,
    ExRightClickConstruction rcc,
    int stage,
    IReadOnlyDictionary<string, string> materials
  ) {
    var wanted = new Dictionary<string, string>(materials);
    var stacks = new List<ItemStack>();
    foreach (
      ExConstructionIngredient each in rcc.Stages[stage].RequireStacks ?? []
    ) {
      ExConstructionIngredient ingredient = each.Clone();
      foreach ((string stored, string value) in rcc.StoredWildCards)
        ingredient.FillPlaceHolder(stored, value);
      ingredient.Resolve(world, "construction walk");
      ItemStack stack =
        Pick(world, ingredient, wanted)
        ?? throw new SetupFileException(
          $"stage {stage} asks for {ingredient.Code}, which no registered {ingredient.Type.ToString().ToLowerInvariant()} supplies"
            + (
              materials.Count == 0
                ? "."
                : $" in {string.Join(", ", materials.Select(m => $"{m.Key} {m.Value}"))}."
            )
        );
      if (
        ingredient.StoreWildCard is { } key
        && stack.Collectible.Variant.TryGetValue(key, out string? variant)
      )
        wanted[key] = variant;
      stacks.Add(stack);
    }
    return stacks;
  }

  private static ItemStack? Pick(
    IWorldAccessor world,
    ExConstructionIngredient ingredient,
    IReadOnlyDictionary<string, string> wanted
  ) {
    IEnumerable<CollectibleObject> registered =
      ingredient.Type == EnumItemClass.Item ? world.Items : world.Blocks;
    foreach (CollectibleObject c in registered) {
      if (c?.Code == null || c.Id == 0)
        continue;
      string? variant =
        ingredient.StoreWildCard is { } k
        && c.Variant.TryGetValue(k, out string? v)
          ? v
          : null;
      if (!Allows(ingredient.StoreWildCard, variant, wanted))
        continue;
      var stack = new ItemStack(c, ingredient.Quantity);
      if (ingredient.SatisfiesAsIngredient(stack, false))
        return stack;
    }
    return null;
  }

  /// <summary>The seconds a hold takes to finish every hit of <paramref name="step"/> with
  /// <paramref name="tool"/>.</summary>
  /// <param name="step">The tool step being paid.</param>
  /// <param name="tool">The stack in the main hand; null when the step's tool is not held.</param>
  public static double HoldSeconds(ExConstructionToolStep step, ItemStack? tool) {
    float stroke =
      CraftSequenceRegistry
        .Shared.ToolDefault(
          tool?.Collectible.Code.ToString() ?? step.Tool ?? ""
        )
        ?.Stroke
      ?? CraftToolDefault.DefaultStroke;
    return (Math.Max(1, step.Hits) * (double)stroke) + 0.1;
  }

  /// <summary>The stack each hotbar slot holds for a construction click.</summary>
  /// <param name="slots">The hotbar's slot count.</param>
  /// <param name="hand">The active slot's index, 0 to <paramref name="slots"/> - 1.</param>
  /// <param name="supply">The stage's materials, in order.</param>
  /// <param name="tool">The stack for the active slot; null puts the first supply stack there instead.</param>
  public static ItemStack?[] Lay(
    int slots,
    int hand,
    IReadOnlyList<ItemStack> supply,
    ItemStack? tool
  ) {
    var laid = new ItemStack?[slots];
    int next = 0;
    laid[hand] = tool ?? (supply.Count > 0 ? supply[next++] : null);
    for (int i = 0; i < slots; i++)
      if (i != hand)
        laid[i] = next < supply.Count ? supply[next++] : null;
    return laid;
  }
}
