using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
#if GAME_GE_1_22
using Vintagestory.API.Datastructures;
#endif

namespace ExpandedLib.Checks;

/// <summary>The in-game <see cref="ICheckSource"/> and <see cref="ILoadedGame"/>: codes and recipes
/// from <see cref="ICoreAPI.Assets"/>, recipes but those a mod removed after loading, definitions
/// from <see cref="ExDefinitions"/>, collectibles and recipe outputs from the world. A domain is exlib and every mod that depends on it.</summary>
/// <remarks>The blocks and items the game fills in for codes a save maps and nothing registers
/// (<see cref="CollectibleObject.IsMissing"/>) are left out of every code and collectible it
/// yields.</remarks>
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
  public IEnumerable<AssetLocation> BlockCodes => Blocks.Select(b => b.Code);

  /// <inheritdoc/>
  public IEnumerable<AssetLocation> ItemCodes => Items.Select(i => i.Code);

  private IEnumerable<Block> Blocks =>
    api.World.Blocks.Where(b => b?.Code != null && !b.IsMissing);

  private IEnumerable<Item> Items =>
    api.World.Items.Where(i => i?.Code != null && !i.IsMissing);

  /// <inheritdoc/>
  /// <remarks>Every recipe but those a mod removed after loading. A grid, smithing, clay forming,
  /// knapping or barrel recipe is left out when its registry held a recipe of the same
  /// <c>Name</c> whose output code the file's matches when the server finished loading assets
  /// (exlib's <c>AssetsFinalize</c>) and holds none now, each <c>{name}</c> in the file's code
  /// standing for any text. The game names a recipe by its JSON <c>name</c>, in the file's
  /// domain, else by its file. A recipe the game refused at load, one with no output code, one of
  /// another folder, and every recipe of a game whose registries were never noted, are read as
  /// their files have them.</remarks>
  public IEnumerable<(AssetLocation File, JObject Json)> Recipes(string domain) {
    foreach (IAsset asset in api.Assets.GetMany("recipes/", domain))
      foreach (JObject recipe in ReadRecipeObjects(asset))
        if (!IsRemoved(asset.Location, recipe))
          yield return (asset.Location, recipe);
  }

  /// <summary>Notes which grid, smithing, clay forming, knapping and barrel recipes
  /// <paramref name="api"/>'s registries hold, replacing an earlier note; <see cref="Recipes"/>
  /// leaves out a noted recipe the registries no longer hold. Called on the server once the game's
  /// recipe loaders have filled the registries and before any mod's <c>StartServerSide</c> removes
  /// one.</summary>
  internal static void NoteHeldRecipes(ICoreAPI api) =>
    HeldAtLoad.AddOrUpdate(api, HeldRecipes(api));

  private static readonly ConditionalWeakTable<
    ICoreAPI,
    Dictionary<string, ILookup<string, AssetLocation>>
  > HeldAtLoad = new();

  private static readonly Regex Placeholder = new(@"\{[^}]*\}");

  private Dictionary<string, ILookup<string, AssetLocation>>? _heldNow;

  private bool IsRemoved(AssetLocation file, JObject recipe) {
    string[] folders = file.Path.Split('/');
    if (
      folders.Length < 3
      || !HeldAtLoad.TryGetValue(
        api,
        out Dictionary<string, ILookup<string, AssetLocation>>? atLoad
      )
      || !atLoad.TryGetValue(
        folders[1],
        out ILookup<string, AssetLocation>? held
      )
      || recipe.GetValue("output", StringComparison.OrdinalIgnoreCase)
        is not JObject output
      || output.GetValue("code", StringComparison.OrdinalIgnoreCase)
        is not JValue { Type: JTokenType.String } code
    )
      return false;
    string name = (
      recipe.GetValue("name", StringComparison.OrdinalIgnoreCase)
        is JValue { Type: JTokenType.String } named
        ? AssetLocation.Create((string)named!, file.Domain)
        : file
    ).ToString();
    var made = AssetLocation.Create(
      Placeholder.Replace((string)code!, "*"),
      file.Domain
    );
    return held[name].Any(o => WildcardUtil.Match(made, o))
      && !(
        (_heldNow ??= HeldRecipes(api)).TryGetValue(
          folders[1],
          out ILookup<string, AssetLocation>? now
        ) && now[name].Any(o => WildcardUtil.Match(made, o))
      );
  }

  // Per recipe folder, each held recipe's output code under its Name, matched case-insensitively.
  private static Dictionary<string, ILookup<string, AssetLocation>> HeldRecipes(
    ICoreAPI api
  ) {
    var held = new Dictionary<string, ILookup<string, AssetLocation>>(
      StringComparer.Ordinal
    );
    void Add(
      string folder,
      IEnumerable<(AssetLocation? Name, AssetLocation? Output)> recipes
    ) =>
      held[folder] = recipes
        .Where(r => r.Name != null && r.Output != null)
        .ToLookup(
          r => r.Name!.ToString(),
          r => r.Output!,
          StringComparer.OrdinalIgnoreCase
        );
    if (api.World.GridRecipes is { } grid)
      Add("grid", grid.Select(r => (r.Name, r.Output?.Code)));
    if (api.ModLoader.GetModSystem<RecipeRegistrySystem>() is { } registry) {
      Add(
        "smithing",
        registry.SmithingRecipes.Select(r => (r.Name, r.Output?.Code))
      );
      Add(
        "clayforming",
        registry.ClayFormingRecipes.Select(r => (r.Name, r.Output?.Code))
      );
      Add(
        "knapping",
        registry.KnappingRecipes.Select(r => (r.Name, r.Output?.Code))
      );
      Add(
        "barrel",
        registry.BarrelRecipes.Select(r => (r.Name, r.Output?.Code))
      );
    }
    return held;
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
    Blocks.Cast<CollectibleObject>().Concat(Items);

  /// <inheritdoc/>
  /// <remarks>The recipe registries yield only the grid recipes when the game runs no
  /// <c>RecipeRegistrySystem</c>.</remarks>
  public IEnumerable<LoadedOutput> RecipeOutputs {
    get {
      foreach (LoadedOutput made in Catalogued())
        yield return made;
      foreach (GridRecipe recipe in api.World.GridRecipes ?? [])
        if (recipe.Output?.Code != null)
          yield return new("grid", recipe.Output.Type, recipe.Output.Code);
      if (
        api.ModLoader.GetModSystem<RecipeRegistrySystem>() is not { } registry
      )
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

  // The items exlib's catalogues name as made: each terminal job's output, each stopping point
  // of a stock route, and each loaded die's job output.
  private IEnumerable<LoadedOutput> Catalogued() {
    foreach (string machine in ProcessJobRegistry.Shared.Machines)
      foreach (ProcessJob job in ProcessJobRegistry.Shared.Jobs(machine))
        yield return new("processjobs", EnumItemClass.Item, new(job.Output));
    foreach (string family in ProcessRouteRegistry.Shared.Families)
      foreach (
        ProcessStage stage in ProcessRouteRegistry.Shared.Route(family)?.Stages
          ?? []
      )
        if (stage.IsStoppingPoint)
          yield return new("processroutes", EnumItemClass.Item, new(stage.Code!));
    foreach (Item item in Items)
      if (
        ItemDie.TryParse(
          item.Attributes?[ItemDie.AttributeKey],
          out ProcessJobSet? set,
          out _
        )
      )
        foreach (ProcessJob job in set!.Jobs)
          yield return new("die", EnumItemClass.Item, new(job.Output));
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
