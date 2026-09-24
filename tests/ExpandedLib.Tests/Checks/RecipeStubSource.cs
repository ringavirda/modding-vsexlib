using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Tests;

/// <summary>An <see cref="ICheckSource"/> holding planted grid recipes and JSON blocktypes, each
/// under the domain of its file; it registers no codes and ships no lang.</summary>
internal sealed class RecipeStubSource : ICheckSource {
  private readonly List<(AssetLocation File, JObject Json)> _recipes = [];
  private readonly List<(AssetLocation File, JObject Json)> _types = [];
  private readonly List<ExBlockDef> _defs = [];
  private string[]? _covered;

  /// <summary>Adds <paramref name="json"/>, one recipe object, as a recipe in
  /// <paramref name="file"/>, e.g. <c>"stub:recipes/grid/a.json"</c>.</summary>
  public RecipeStubSource Recipe(string file, string json) {
    _recipes.Add((new AssetLocation(file), JObject.Parse(json)));
    return this;
  }

  /// <summary>Adds <paramref name="json"/> as a JSON blocktype in
  /// <paramref name="file"/>.</summary>
  public RecipeStubSource BlockType(string file, string json) {
    _types.Add((new AssetLocation(file), JObject.Parse(json)));
    return this;
  }

  /// <summary>Adds a code-first block definition.</summary>
  public RecipeStubSource Definition(ExBlockDef def) {
    _defs.Add(def);
    return this;
  }

  /// <summary>Limits <see cref="Domains"/> to <paramref name="domains"/>; by default it is every
  /// domain a recipe, blocktype or definition is filed under.</summary>
  public RecipeStubSource Covering(params string[] domains) {
    _covered = domains;
    return this;
  }

  public IEnumerable<string> Domains =>
    _covered
    ?? _recipes
      .Concat(_types)
      .Select(r => r.File.Domain)
      .Concat(_defs.Select(d => d.Domain))
      .Distinct();

  public IEnumerable<AssetLocation> BlockCodes => [];
  public IEnumerable<AssetLocation> ItemCodes => [];

  public IEnumerable<(AssetLocation File, JObject Json)> Recipes(
    string domain
  ) => _recipes.Where(r => r.File.Domain == domain);

  public IEnumerable<(string Locale, JObject Json)> Lang(string domain) => [];

  public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
    _defs.Where(d => d.Domain == domain);

  public IEnumerable<(AssetLocation File, JObject Json)> BlockTypes(
    string domain
  ) => _types.Where(t => t.File.Domain == domain);
}
