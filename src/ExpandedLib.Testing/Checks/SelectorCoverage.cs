using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>
/// The two selector families anchored on an emitted block code, both of which fail silently when the
/// code moves out from under them: <c>shapeByType</c>, which leaves a variant with no shape, and the
/// handbook's <c>groupBy</c>, which leaves an entry ungrouped. Inserting a variant group ahead of an
/// existing one is how a code moves, and neither an unmatched shape pattern nor an unmatched group
/// selector is an error.
/// <para>
/// The goldens are the emitted blocktypes, so checking them covers the code-first definitions without
/// needing a running game.
/// </para>
/// </summary>
public static class SelectorCoverage {
  /// <summary>
  /// Checks one golden blocktype against both selector rules. <c>shapeByType</c> is self-contained: every
  /// code the golden's own <c>variantgroups</c> produce must match one of its patterns. The handbook's
  /// <c>groupBy</c> selects across blocktypes - the four pipe segments each list all four of their tier's
  /// shapes - so a selector is checked against every code the golden's own domain emits, not just this
  /// golden's. A selector with no domain is qualified with the golden's own before matching
  /// (<c>SlideshowItemstackTextComponent</c> does the same at render time). Empty in both means
  /// clean.
  /// </summary>
  public static (
    IReadOnlyList<string> shapeByType,
    IReadOnlyList<string> groupBy
  ) Check(string goldenBlocktypePath) {
    var shapeByType = new List<string>();
    var groupBy = new List<string>();

    using JsonDocument doc = Parse(goldenBlocktypePath);
    JsonElement root = doc.RootElement;
    if (root.ValueKind != JsonValueKind.Object)
      return (shapeByType, groupBy);

    JsonElement shapes = ShapeByType(root);
    if (shapes.ValueKind == JsonValueKind.Object) {
      var patterns = shapes.EnumerateObject().Select(p => p.Name).ToList();
      var unmatched = BlockCodes(root)
        .Where(code => !patterns.Any(pat => WildcardUtil.Match(pat, code)))
        .ToList();
      if (unmatched.Count > 0)
        shapeByType.Add(
          $"{goldenBlocktypePath}: {unmatched.Count} variant(s) match no shape pattern "
            + $"[{string.Join(", ", patterns)}]: {string.Join(", ", unmatched.Take(6))}"
        );
    }

    var selectors = HandbookGroups(root);
    if (selectors.Count > 0) {
      string domain = DomainOf(goldenBlocktypePath);
      IReadOnlyList<string> corpus = QualifiedCodes(domain);
      var unmatched = selectors
        .Where(sel => {
          string qualified = sel.Contains(':') ? sel : $"{domain}:{sel}";
          return !corpus.Any(code => WildcardUtil.Match(qualified, code));
        })
        .ToList();
      if (unmatched.Count > 0)
        groupBy.Add(
          $"{goldenBlocktypePath}: {unmatched.Count} handbook groupBy selector(s) match no shipped "
            + $"block code (resolved against domain {domain}): {string.Join(", ", unmatched)}"
        );
    }

    return (shapeByType, groupBy);
  }

  /// <summary>
  /// Every golden blocktype JSON <paramref name="domain"/> ships - <c>&lt;mod or sample tests&gt;/goldens/
  /// &lt;domain&gt;/blocktypes/**</c> under every mod and sample the manifest names, repo-relative and
  /// forward-slashed. The corpus <see cref="Check"/> runs over, and what a caller asserts non-empty as
  /// the corpus-integrity premise.
  /// </summary>
  public static IReadOnlyList<string> GoldenBlocktypes(string domain) {
    string root = RepoPaths.Root;
    var marker = $"/goldens/{domain}/blocktypes/";
    var files = new List<string>();

    foreach (string tests in TestRoots()) {
      if (!Directory.Exists(tests))
        continue;
      foreach (
        string file in Directory.EnumerateFiles(
          tests,
          "*.json",
          SearchOption.AllDirectories
        )
      ) {
        string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        if (rel.Contains("/bin/") || rel.Contains("/obj/"))
          continue;
        if (!rel.Contains(marker, StringComparison.Ordinal))
          continue;
        files.Add(rel);
      }
    }
    return files;
  }

  // Every mod's own tests folder, every sample's (kept separate because a sample's tests project
  // need not sit under its own path - exmod.json's samples entry names it independently), plus
  // every $Manifest.tests entry - exlib's own ExpandedLib.Tests sits outside any mod folder.
  private static IEnumerable<string> TestRoots() {
    foreach (string mod in RepoManifest.Mods.Values)
      yield return Path.Combine(mod, "tests");
    foreach (RepoManifest.SampleEntry sample in RepoManifest.Samples.Values)
      yield return sample.Tests;
    foreach (string tests in RepoManifest.Tests)
      yield return tests;
  }

  // Built once per domain: the groupBy rule needs the whole domain's codes and every golden in that
  // domain re-asks for it.
  private static readonly ConcurrentDictionary<
    string,
    IReadOnlyList<string>
  > _qualifiedCodesByDomain = new();

  private static IReadOnlyList<string> QualifiedCodes(string domain) =>
    _qualifiedCodesByDomain.GetOrAdd(domain, ComputeQualifiedCodes);

  private static IReadOnlyList<string> ComputeQualifiedCodes(string domain) {
    var codes = new List<string>();
    foreach (string path in GoldenBlocktypes(domain)) {
      using JsonDocument doc = Parse(path);
      foreach (string code in BlockCodes(doc.RootElement))
        codes.Add($"{domain}:{code}");
    }
    return codes;
  }

  /// <summary>The <c>shapeByType</c> map a blocktype declares (either casing), or a default element when
  /// it declares none.</summary>
  private static JsonElement ShapeByType(JsonElement root) {
    foreach (JsonProperty p in root.EnumerateObject())
      if (p.NameEquals("shapebytype") || p.NameEquals("shapeByType"))
        return p.Value;
    return default;
  }

  /// <summary>The <c>attributes.handbook.groupBy</c> selectors a blocktype declares, or empty.</summary>
  private static List<string> HandbookGroups(JsonElement root) {
    if (
      root.ValueKind != JsonValueKind.Object
      || !root.TryGetProperty("attributes", out JsonElement attrs)
      || !attrs.TryGetProperty("handbook", out JsonElement handbook)
      || !handbook.TryGetProperty("groupBy", out JsonElement groups)
      || groups.ValueKind != JsonValueKind.Array
    )
      return [];

    return [.. groups.EnumerateArray().Select(g => g.GetString() ?? "")];
  }

  // goldens/<domain>/blocktypes/... - the golden JSON carries the bare code, not the domain.
  private static string DomainOf(string repoRelativePath) {
    string[] parts = repoRelativePath.Split('/');
    int i = Array.IndexOf(parts, "goldens");
    return i >= 0 && i + 1 < parts.Length ? parts[i + 1] : "";
  }

  /// <summary>Every full block code the definition's variant groups produce.</summary>
  private static IEnumerable<string> BlockCodes(JsonElement root) {
    string code = root.TryGetProperty("code", out JsonElement c)
      ? c.GetString() ?? ""
      : "";
    if (code.Length == 0)
      yield break;

    var axes = new List<string[]>();
    if (root.TryGetProperty("variantgroups", out JsonElement groups))
      foreach (JsonElement g in groups.EnumerateArray()) {
        if (g.TryGetProperty("states", out JsonElement states))
          axes.Add([
            .. states.EnumerateArray().Select(s => s.GetString() ?? ""),
          ]);
        else
          // The only loadFromProperties in use is the horizontal orientation.
          axes.Add(["north", "south", "east", "west"]);
      }

    IEnumerable<string> codes = [code];
    foreach (string[] axis in axes)
      codes = codes.SelectMany(prefix => axis.Select(v => prefix + "-" + v));
    foreach (string full in codes)
      yield return full;
  }

  private static JsonDocument Parse(string repoRelativePath) =>
    JsonDocument.Parse(
      File.ReadAllText(Path.Combine(RepoPaths.Root, repoRelativePath)),
      new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );
}
