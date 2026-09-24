using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
#if GAME_GE_1_22
using Vintagestory.API.Datastructures;
#endif

namespace ExpandedLib.Checks;

/// <summary>
/// The in-game <see cref="ICheckSource"/> and <see cref="ILoadedGame"/>: codes and recipes from
/// <see cref="ICoreAPI.Assets"/>, block definitions from the process-wide
/// <see cref="ExDefinitions"/> registry, collectibles and recipe outputs from the world. A domain
/// is exlib and every enabled mod that depends on it.
/// </summary>
public sealed class AssetCheckSource(ICoreAPI api) : ILoadedGame {
  /// <inheritdoc/>
  public IEnumerable<string> Domains =>
    api
      .ModLoader.Mods.Where(m =>
        m.Info.ModID == "exlib"
        || m.Info.Dependencies.Any(d => d.ModID == "exlib")
      )
      .Select(m => m.Info.ModID)
      .Distinct();

  /// <inheritdoc/>
  public IEnumerable<AssetLocation> BlockCodes =>
    api.World.Blocks.Where(b => b?.Code != null).Select(b => b.Code);

  /// <inheritdoc/>
  public IEnumerable<AssetLocation> ItemCodes =>
    api.World.Items.Where(i => i?.Code != null).Select(i => i.Code);

  /// <inheritdoc/>
  public IEnumerable<(AssetLocation File, JObject Json)> Recipes(string domain) {
    foreach (IAsset asset in api.Assets.GetMany("recipes/", domain))
      foreach (JObject recipe in ReadRecipeObjects(asset))
        yield return (asset.Location, recipe);
  }

  /// <inheritdoc/>
  public IEnumerable<(string Locale, JObject Json)> Lang(string domain) {
    foreach (IAsset asset in api.Assets.GetMany("lang/", domain)) {
      if (TryParseObject(asset, out JObject json))
        yield return (
          Path.GetFileNameWithoutExtension(asset.Location.Path),
          json
        );
    }
  }

  /// <inheritdoc/>
  public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
    ExDefinitions.Blocks.Where(d => d.Domain == domain);

  /// <inheritdoc/>
  public IEnumerable<(AssetLocation File, JObject Json)> BlockTypes(
    string domain
  ) {
    foreach (IAsset asset in api.Assets.GetMany("blocktypes/", domain))
      if (TryParseObject(asset, out JObject json))
        yield return (asset.Location, json);
  }

  /// <inheritdoc/>
  public IEnumerable<(AssetLocation File, JObject Json)> ItemTypes(
    string domain
  ) {
    foreach (IAsset asset in api.Assets.GetMany("itemtypes/", domain))
      if (TryParseObject(asset, out JObject json))
        yield return (asset.Location, json);
  }

  /// <inheritdoc/>
  public IEnumerable<CollectibleObject> Collectibles =>
    api
      .World.Blocks.Where(b => b?.Code != null)
      .Cast<CollectibleObject>()
      .Concat(api.World.Items.Where(i => i?.Code != null));

  /// <inheritdoc/>
  /// <remarks>Only the grid recipes when the game runs no <c>RecipeRegistrySystem</c>.</remarks>
  public IEnumerable<LoadedOutput> RecipeOutputs {
    get {
      foreach (GridRecipe recipe in api.World.GridRecipes ?? [])
        if (recipe.Output?.Code != null)
          yield return new("grid", recipe.Output.Type, recipe.Output.Code);
      if (api.ModLoader.GetModSystem<RecipeRegistrySystem>() is not { } registry)
        yield break;
      foreach (CookingRecipe recipe in registry.CookingRecipes)
        if (Made("cooking", recipe.CooksInto) is { } made)
          yield return made;
      foreach (BarrelRecipe recipe in registry.BarrelRecipes)
        if (Made("barrel", recipe.Output) is { } made)
          yield return made;
      foreach (AlloyRecipe recipe in registry.MetalAlloys)
        if (Made("alloy", recipe.Output) is { } made)
          yield return made;
      foreach (SmithingRecipe recipe in registry.SmithingRecipes)
        if (Made("smithing", recipe.Output) is { } made)
          yield return made;
      foreach (KnappingRecipe recipe in registry.KnappingRecipes)
        if (Made("knapping", recipe.Output) is { } made)
          yield return made;
      foreach (ClayFormingRecipe recipe in registry.ClayFormingRecipes)
        if (Made("clayforming", recipe.Output) is { } made)
          yield return made;
    }
  }

  private static LoadedOutput? Made(string registry, JsonItemStack? stack) =>
    stack?.Code == null ? null : new(registry, stack.Type, stack.Code);

  /// <inheritdoc/>
  public IReadOnlyList<AssetLocation>? Tagged(JObject ingredient) {
#if GAME_GE_1_22
    if (
      ingredient.GetValue("code", StringComparison.OrdinalIgnoreCase) != null
      || ingredient.GetValue("tags", StringComparison.OrdinalIgnoreCase)
        is not { } tags
    )
      return null;
    var condition = tags.ToObject<ComplexTagCondition<TagSet>>();
    EnumItemClass type =
      (string?)ingredient.GetValue("type", StringComparison.OrdinalIgnoreCase)
        is { } named
      && named.Equals("item", StringComparison.OrdinalIgnoreCase)
        ? EnumItemClass.Item
        : EnumItemClass.Block;
    return
    [
      .. Collectibles
        .Where(c => c.ItemClass == type && condition.Matches(c.Tags))
        .Select(c => c.Code),
    ];
#else
    return null;
#endif
  }

  /// <inheritdoc/>
  public Type? BlockClass(string classKey) =>
    api.ClassRegistry.GetBlockClass(classKey);

  /// <inheritdoc/>
  public Type? BlockEntityBehaviorClass(string key) =>
    api.ClassRegistry.GetBlockEntityBehaviorClass(key);

  // A recipe file is one object or a JSON array; every element shares the file's location.
  private static IEnumerable<JObject> ReadRecipeObjects(IAsset asset) {
    if (!TryParseToken(asset, out JToken token))
      yield break;

    foreach (JToken item in token is JArray array ? array : [token])
      if (item is JObject obj)
        yield return obj;
  }

  private static bool TryParseObject(IAsset asset, out JObject json) {
    json = null!;
    if (!TryParseToken(asset, out JToken token) || token is not JObject obj)
      return false;
    json = obj;
    return true;
  }

  // A malformed file is skipped, not reported: the asset loader already logs the parse failure.
  private static bool TryParseToken(IAsset asset, out JToken token) {
    token = null!;
    try {
      token = JToken.Parse(asset.ToText());
      return true;
    } catch (Newtonsoft.Json.JsonException) {
      return false;
    }
  }
}
