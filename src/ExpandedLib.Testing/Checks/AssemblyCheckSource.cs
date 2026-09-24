using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// An <see cref="ICheckSource"/> built directly from <c>(domain, Assembly)</c> pairs, with no
/// repo-root probing and no file-system lang lookup beyond the directory a caller hands it.
/// </summary>
internal sealed class AssemblyCheckSource : ICheckSource {
  private readonly Dictionary<string, Assembly> _assemblies;
  private readonly Dictionary<string, string> _langDirs;

  internal AssemblyCheckSource(
    params (string Domain, Assembly Assembly)[] sources
  )
    : this(sources, []) { }

  internal AssemblyCheckSource(
    (string Domain, Assembly Assembly)[] sources,
    (string Domain, string LangDir)[] langDirs
  ) {
    _assemblies = sources.ToDictionary(
      s => s.Domain,
      s => s.Assembly,
      StringComparer.Ordinal
    );
    _langDirs = langDirs.ToDictionary(
      l => l.Domain,
      l => l.LangDir,
      StringComparer.Ordinal
    );
  }

  /// <summary>Yields a worldproperty-sourced variant group in <see cref="BlockCodes"/> as a literal
  /// <c>*</c> segment instead of its sampled states, so a code naming any state of it
  /// matches.</summary>
  internal bool PropertyGroupsAsWildcard { get; init; }

  public IEnumerable<string> Domains => _assemblies.Keys;

  public IEnumerable<AssetLocation> BlockCodes =>
    _assemblies.SelectMany(kv =>
      PropertyGroupsAsWildcard
        ? DefinitionCodes
          .PatternsForDomain(kv.Key, kv.Value)
          .Select(c => new AssetLocation(c))
        : DefinitionCodes
          .ForDomain(kv.Key, kv.Value)
          .Select(r => new AssetLocation(r.Code))
    );

  public IEnumerable<AssetLocation> ItemCodes =>
    _assemblies.SelectMany(kv =>
      DefinitionCatalogue
        .ItemPatterns(kv.Key, kv.Value)
        .Select(p => new AssetLocation(p))
    );

  public IEnumerable<(AssetLocation File, JObject Json)> Recipes(string domain) {
    foreach (
      ExRecipeDef def in DefinitionGoldens
        .Collect(domain, _assemblies[domain])
        .OfType<ExRecipeDef>()
    ) {
      JToken json = def.ToJson();
      foreach (JToken recipe in json is JArray arr ? arr : [json])
        if (recipe is JObject obj)
          yield return (def.Location, obj);
    }
  }

  public IEnumerable<(string Locale, JObject Json)> Lang(string domain) {
    if (
      !_langDirs.TryGetValue(domain, out string? dir) || !Directory.Exists(dir)
    )
      yield break;

    foreach (
      string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f)
    )
      yield return (
        Path.GetFileNameWithoutExtension(file),
        JObject.Parse(File.ReadAllText(file))
      );
  }

  public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
    DefinitionGoldens.Collect(domain, _assemblies[domain]).OfType<ExBlockDef>();
}
