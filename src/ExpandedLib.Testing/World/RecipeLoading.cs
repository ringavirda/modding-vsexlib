#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace ExpandedLib.Testing;

/// <summary>
/// Recipes in <see cref="TestWorld"/>: the game's own recipe loader run over the assets a load
/// mirrored, and the recipe lists and ingredient index the game's recipes read.
/// </summary>
public sealed partial class TestWorld
{
  private readonly List<GridRecipe> _gridRecipes = [];
  private readonly List<SmithingRecipe> _smithingRecipes = [];
  private readonly List<ClayFormingRecipe> _clayFormingRecipes = [];

  private readonly System.Collections.Generic.OrderedDictionary<
    IRecipeIngredientBase,
    List<IRecipeBase>
  > _fastSearchRecipes = new();

  private IAssetManager? _loadedAssets;
  private IAssetOrigin? _gameSurvival;

  private static readonly string[] RecipePaths =
  [
    "recipes/grid",
    "recipes/smithing",
    "recipes/clayforming",
  ];

  /// <summary>The smithing recipes <see cref="LoadRecipes"/> kept, in load order.</summary>
  public IReadOnlyList<SmithingRecipe> SmithingRecipes => _smithingRecipes;

  /// <summary>The clay forming recipes <see cref="LoadRecipes"/> kept, in load order.</summary>
  public IReadOnlyList<ClayFormingRecipe> ClayFormingRecipes =>
    _clayFormingRecipes;

  /// <summary>Loads the grid, smithing and clay forming recipes of the assets the last
  /// <c>LoadAssets</c> or <c>LoadAsGame</c> mirrored, and the game's own grid recipes, through the
  /// game's own <c>RecipeLoader.LoadRecipe</c>, then runs exlib's recipe-cost pass at each
  /// registered profile's selected level.</summary>
  /// <remarks>Grid recipes land in <c>World.GridRecipes</c> on the server and client worlds. A
  /// second call empties the lists and the ingredient index first. The game's smithing and clay
  /// forming recipes are not mirrored, so only the listed mods' load, those a mod's definitions
  /// inject included. The game's grid recipes (<c>assets/survival/recipes/grid</c>) load beside the
  /// mods'; one whose ingredients or output the world has not loaded fails to resolve. A recipe that
  /// fails to resolve is the loader's log line in <see cref="Log"/> and a count, never a throw.
  /// Every item and block not yet loaded gets its <c>OnLoadedNative</c> first, since a recipe's
  /// <c>Matches</c> reads the collectible's api.</remarks>
  /// <returns>The recipes kept and the recipes that failed to resolve, over the three
  /// kinds.</returns>
  /// <exception cref="InvalidOperationException">No <c>LoadAssets</c> or <c>LoadAsGame</c> ran on this
  /// world.</exception>
  public (int Loaded, int Failed) LoadRecipes()
  {
    IAssetManager assets =
      _loadedAssets
      ?? throw new InvalidOperationException(
        "LoadRecipes needs LoadAssets or LoadAsGame first."
      );
    foreach (
      CollectibleObject collectible in World
        .Items.Where(i => i?.Code != null)
        .Cast<CollectibleObject>()
        .Concat(World.Blocks.Where(b => b is { Id: not 0 }))
    )
      if (ReflectionHelpers.GetField(collectible, "api") == null)
        collectible.OnLoadedNative(Api);
    MirrorGameGridRecipes(assets);
    _gridRecipes.Clear();
    _smithingRecipes.Clear();
    _clayFormingRecipes.Clear();
    _fastSearchRecipes.Clear();
    bool classExclusive = Config.Tree.GetBool("classExclusiveRecipes", true);

    (int Loaded, int Failed) grid = LoadKind<GridRecipe>(
      assets,
      "grid",
      "recipes/grid",
      classExclusive,
      r => _gridRecipes.Add((GridRecipe)r)
    );
    (int Loaded, int Failed) smithing = LoadKind<SmithingRecipe>(
      assets,
      "smithing",
      "recipes/smithing",
      classExclusive,
      r => _smithingRecipes.Add((SmithingRecipe)r)
    );
    (int Loaded, int Failed) clay = LoadKind<ClayFormingRecipe>(
      assets,
      "clay forming",
      "recipes/clayforming",
      classExclusive,
      r => _clayFormingRecipes.Add((ClayFormingRecipe)r)
    );
    RecipeBase.CollectiblePreSearchResultsCache.Clear();
    ExRecipeProfiles.ApplyAll(Api);
    return (
      grid.Loaded + smithing.Loaded + clay.Loaded,
      grid.Failed + smithing.Failed + clay.Failed
    );
  }

  private void MirrorGameGridRecipes(IAssetManager assets)
  {
    if (_gameSurvival == null || assets is not AssetManager manager)
      return;
    foreach (
      IAsset asset in _gameSurvival.GetAssets(
        AssetCategory.recipes,
        shouldLoad: true
      )
    )
      if (
        asset.Location.Path.StartsWith(
          "recipes/grid/",
          StringComparison.Ordinal
        ) && !manager.AllAssets.ContainsKey(asset.Location)
      )
        manager.Add(asset.Location, asset);
  }

  private static readonly DiscardLogger QuietLogger = new();

  private sealed class DiscardLogger : LoggerBase
  {
    protected override void LogImpl(
      EnumLogType logType,
      string format,
      params object[] args
    ) { }
  }

  private void SetRecipeLogger(ILogger logger)
  {
    Api.Logger.Returns(logger);
    ((ICoreAPI)Api).Logger.Returns(logger);
    World.Logger.Returns(logger);
  }

  private (int Loaded, int Failed) LoadKind<T>(
    IAssetManager assets,
    string name,
    string path,
    bool classExclusive,
    Action<IRecipeBase> keep
  )
    where T : IRecipeBase
  {
    int loaded = 0;
    int failed = 0;
    foreach (
      (AssetLocation location, JToken content) in assets.GetMany<JToken>(
        Log,
        path
      )
    )
    {
      IEnumerable<JToken> tokens = content switch
      {
        JObject o => [o],
        JArray a => a,
        _ => [],
      };
      bool vanilla = location.Domain == "game";
      if (vanilla)
        SetRecipeLogger(QuietLogger);
      try
      {
        foreach (JToken token in tokens)
        {
          T? recipe = token.ToObject<T>(location.Domain);
          if (recipe == null)
          {
            Log.Error($"Failed to parse {name} recipe: {location}");
            continue;
          }
          RecipeLoader.LoadRecipe(
            Api,
            location,
            recipe,
            classExclusive,
            keep,
            ref loaded,
            ref failed
          );
        }
      }
      finally
      {
        if (vanilla)
          SetRecipeLogger(Log);
      }
    }
    return (loaded, failed);
  }

  /// <summary>The <c>game:</c> codes the grid, smithing and clay forming recipe assets in
  /// <paramref name="assets"/> name, wildcards and placeholders as written; a code with no domain
  /// takes the domain of the file that names it, as the game's loader reads it.</summary>
  private IReadOnlyCollection<string> RecipeVanillaCodes(IAssetManager assets)
  {
    var codes = new HashSet<string>();
    foreach (string path in RecipePaths)
    foreach (
      (AssetLocation file, JToken content) in assets.GetMany<JToken>(Log, path)
    )
    foreach (JToken token in content.SelectTokens("$..code"))
      if (
        token is JValue { Value: string code }
        && AssetLocation.Create(code, file.Domain)
          is { Domain: "game", Path: { Length: > 0 } } location
      )
        codes.Add($"game:{location.Path}");
    return codes;
  }

  private static void WireRecipeMatching(IEventAPI events)
  {
    var gridHandlers = new List<MatchGridRecipeDelegate>();
    var recipeHandlers = new List<MatchRecipeDelegate>();
    events
      .When(e => e.MatchesGridRecipe += Arg.Any<MatchGridRecipeDelegate>())
      .Do(ci => gridHandlers.Add(ci.Arg<MatchGridRecipeDelegate>()));
    events
      .When(e => e.MatchesRecipe += Arg.Any<MatchRecipeDelegate>())
      .Do(ci => recipeHandlers.Add(ci.Arg<MatchRecipeDelegate>()));
    events
      .TriggerMatchesRecipe(
        Arg.Any<IPlayer>(),
        Arg.Any<GridRecipe>(),
        Arg.Any<ItemSlot[]>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        gridHandlers.All(h =>
          h(
            ci.Arg<IPlayer>(),
            ci.Arg<GridRecipe>(),
            ci.Arg<ItemSlot[]>(),
            ci.Arg<int>()
          )
        )
      );
    events
      .TriggerMatchesRecipe(
        Arg.Any<IPlayer>(),
        Arg.Any<IRecipeBase>(),
        Arg.Any<ItemSlot[]>()
      )
      .Returns(ci =>
        recipeHandlers.All(h =>
          h(ci.Arg<IPlayer>(), ci.Arg<IRecipeBase>(), ci.Arg<ItemSlot[]>())
        )
      );
  }
}
#endif
