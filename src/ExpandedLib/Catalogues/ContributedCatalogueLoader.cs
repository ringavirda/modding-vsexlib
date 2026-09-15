using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>
/// Base for a JSON catalogue with C# contributors: reads every domain's files under one asset
/// path, audits their keys, parses each once, merges into the registry, invokes the contributors.
/// </summary>
/// <typeparam name="TSet">One file's parsed declaration (a route, a job set, an occupancy set).</typeparam>
/// <typeparam name="TRegistry">The instance registry the parsed sets merge into.</typeparam>
public abstract class ContributedCatalogueLoader<TSet, TRegistry> {
  /// <summary>The asset path (with trailing slash) every domain's files are read from.</summary>
  protected abstract string AssetPath { get; }

  /// <summary>The catalogue's own name, for <see cref="CatalogueLoadReport"/>.</summary>
  protected abstract string CatalogueName { get; }

  /// <summary>Every key on <paramref name="root"/> the schema does not read, root and nested.
  /// Empty when the file is clean.</summary>
  protected abstract IReadOnlyList<string> UnknownKeys(JsonObject root);

  /// <summary>Parses one already key-audited file. Returns false with a human-readable
  /// <paramref name="error"/> on any malformed field.</summary>
  protected abstract bool TryParse(
    JsonObject root,
    out TSet set,
    out string? error
  );

  /// <summary>Merges <paramref name="set"/> into <paramref name="registry"/>. Returns one message per
  /// clash naming what lost; the caller attaches the file it came from.</summary>
  protected abstract IReadOnlyList<string> Contribute(
    TRegistry registry,
    TSet set
  );

  /// <summary>How many records <paramref name="set"/> counts as, for the report.</summary>
  protected abstract int CountEntries(TSet set);

  /// <summary>The code contributions re-invoked after every load.</summary>
  protected abstract CatalogueContributors Contributors(TRegistry registry);

  /// <summary>Empties <paramref name="registry"/> before repopulating it.</summary>
  protected abstract void Clear(TRegistry registry);

  // One file that parsed, paired with the source it came from.
  private readonly record struct Parsed(TSet Set, string Source);

  // The per-file cycle: JSON syntax, the schema's key audit, then the domain parse.
  private List<Parsed> ParseInternal(
    IEnumerable<(string Source, string Json)> files,
    List<string> errors
  ) {
    var parsed = new List<Parsed>();
    foreach ((string source, string json) in files) {
      JToken token;
      try {
        token = JToken.Parse(json);
      } catch (Exception e) {
        errors.Add($"{source}: not readable as JSON - {e.Message}");
        continue;
      }

      var root = new JsonObject(token);
      IReadOnlyList<string> unknown = UnknownKeys(root);
      if (unknown.Count > 0) {
        errors.Add($"{source}: unknown key(s) {string.Join(", ", unknown)}");
        continue;
      }

      if (!TryParse(root, out TSet set, out string? error)) {
        errors.Add($"{source}: {error}");
        continue;
      }
      parsed.Add(new Parsed(set, source));
    }
    return parsed;
  }

  /// <summary>
  /// Parses the catalogue out of already-read <c>(source, json)</c> pairs. A malformed file is
  /// reported and skipped. Asset-free; runs headless.
  /// </summary>
  protected List<TSet> ParseFiles(
    IEnumerable<(string Source, string Json)> files,
    out List<string> errors
  ) {
    errors = [];
    return [.. ParseInternal(files, errors).Select(p => p.Set)];
  }

  /// <summary>
  /// Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents with them.
  /// Returns one message per malformed file or clash, each naming the file it came from.
  /// </summary>
  protected List<string> MergeFiles(
    IEnumerable<(string Source, string Json)> files,
    TRegistry registry
  ) {
    var errors = new List<string>();
    List<Parsed> parsed = ParseInternal(files, errors);

    Clear(registry);
    foreach (Parsed p in parsed)
      foreach (string conflict in Contribute(registry, p.Set))
        errors.Add($"{p.Source}: {conflict}");
    return errors;
  }

  /// <summary>Reads every domain's catalogue out of the asset manager.</summary>
  protected List<(string Source, string Json)> ReadAssets(ICoreAPI api) {
    var files = new List<(string, string)>();
    foreach (IAsset asset in api.Assets.GetMany(AssetPath))
      files.Add((asset.Location.ToString(), asset.ToText()));
    return files;
  }

  /// <summary>
  /// Reads the catalogue, repopulates <paramref name="registry"/> and runs its code contributors.
  /// Each file is parsed exactly once.
  /// </summary>
  protected CatalogueLoadReport LoadCatalogue(ICoreAPI api, TRegistry registry) {
    List<(string Source, string Json)> files = ReadAssets(api);
    var errors = new List<string>();
    List<Parsed> parsed = ParseInternal(files, errors);

    Clear(registry);
    int entries = 0;
    foreach (Parsed p in parsed) {
      entries += CountEntries(p.Set);
      foreach (string conflict in Contribute(registry, p.Set))
        errors.Add($"{p.Source}: {conflict}");
    }
    // Must run after Clear, above.
    Contributors(registry).Invoke(api, api.Logger);

    return new CatalogueLoadReport(CatalogueName, files.Count, entries, errors);
  }
}
