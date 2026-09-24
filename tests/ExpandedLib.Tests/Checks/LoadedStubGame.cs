using System;
using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Tests;

/// <summary>An <see cref="ILoadedGame"/> over a <see cref="RecipeStubSource"/>'s recipes, blocktypes
/// and definitions: the blocks and items it is handed are the loaded collectibles, and the outputs
/// it is handed are what the recipe registries make. It resolves no tags.</summary>
internal sealed class LoadedStubGame(RecipeStubSource source) : ILoadedGame {
  private readonly List<CollectibleObject> _collectibles = [];
  private readonly List<LoadedOutput> _outputs = [];

  /// <summary>Adds a loaded block of <paramref name="code"/> with the given variant states, in
  /// group order, and an empty creative tab list, then hands it to
  /// <paramref name="configure"/>.</summary>
  public LoadedStubGame Block(
    string code,
    Action<Block>? configure = null,
    params (string Group, string State)[] variants
  ) => Add(new Block { Code = new AssetLocation(code) }, configure, variants);

  /// <summary>Adds a loaded item, as <see cref="Block"/> adds a block.</summary>
  public LoadedStubGame Item(
    string code,
    Action<Item>? configure = null,
    params (string Group, string State)[] variants
  ) => Add(new Item { Code = new AssetLocation(code) }, configure, variants);

  /// <summary>Adds <paramref name="code"/> as an output of the <c>grid</c> registry.</summary>
  public LoadedStubGame Output(EnumItemClass type, string code) {
    _outputs.Add(new LoadedOutput("grid", type, new AssetLocation(code)));
    return this;
  }

  private LoadedStubGame Add<T>(
    T collectible,
    Action<T>? configure,
    (string Group, string State)[] variants
  )
    where T : CollectibleObject {
    collectible.CreativeInventoryTabs = [];
    foreach ((string group, string state) in variants)
      collectible.VariantStrict[group] = state;
    configure?.Invoke(collectible);
    _collectibles.Add(collectible);
    return this;
  }

  public IEnumerable<CollectibleObject> Collectibles => _collectibles;
  public IEnumerable<LoadedOutput> RecipeOutputs => _outputs;

  public IReadOnlyList<AssetLocation>? Tagged(JObject ingredient) => null;

  public IEnumerable<string> Domains => source.Domains;
  public IEnumerable<AssetLocation> BlockCodes => source.BlockCodes;
  public IEnumerable<AssetLocation> ItemCodes => source.ItemCodes;

  public IEnumerable<(AssetLocation File, JObject Json)> Recipes(
    string domain
  ) => source.Recipes(domain);

  public IEnumerable<(string Locale, JObject Json)> Lang(string domain) => [];

  public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
    source.BlockDefinitions(domain);

  public IEnumerable<(AssetLocation File, JObject Json)> BlockTypes(
    string domain
  ) => source.BlockTypes(domain);
}
