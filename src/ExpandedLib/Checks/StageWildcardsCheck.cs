using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks every <c>ExRightClickConstructable</c> stage table in a domain, JSON and code-first,
/// against vanilla's refund, which fills a paid <c>*</c> from the paid item's
/// <c>Variant[storeWildCard]</c>.
/// </summary>
/// <remarks>
/// Rules, each finding prefixed with its letter: (a) a <c>*</c> carries <c>storeWildCard</c>;
/// (b) the <c>*</c> spans only the key's variant group; (c) the key is a group of every match;
/// (d) the key is <c>wood</c> or <c>metal</c>, the keys a creative Ctrl build stores; (e) a
/// <c>{key}</c> names a block variant group or a key an earlier paid stage stores; (f) a stage 0
/// key is stored by stage 1; (g) a key is stored by one paid stage, since the refund fills every
/// ingredient storing it with the last payment's state, and a later stage takes the stored state
/// as <c>{key}</c>. An ingredient whose code holds its own <c>{key}</c> stores the state it was
/// filled with and is no second store. Ingredients of one stage storing one key are not reported:
/// the stage is one payment, and a later <c>{key}</c> in it would read the state stored before
/// it. (b) and (c) decide block codes in covered domains only;
/// a covered item code is undecidable, and other domains are left to the loaded game.
/// </remarks>
public static class StageWildcardsCheck {
  /// <summary>The <c>storeWildCard</c> keys vanilla's creative Ctrl build stores.</summary>
  private static readonly string[] CreativeKeys = ["wood", "metal"];

  private static readonly Regex Placeholder = new(@"\{(\w+)\}");

  /// <summary>Every rule broken by a stage table in <paramref name="domain"/>.</summary>
  /// <param name="source">The definitions and JSON blocktypes to read, the catalogue that
  /// decides wildcards included.</param>
  /// <param name="domain">The domain whose stage tables are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>StageWildcards</c>, one error per
  /// violation prefixed with its rule; no errors when none.</returns>
  /// <exception cref="ArgumentException">A code, type, key, state or variant list the tables
  /// read is a JSON object or array where a string is read, or an array where an object is
  /// read.</exception>
  /// <exception cref="InvalidOperationException">A behaviour, its properties, a stage or an
  /// ingredient is a JSON value where an object is read.</exception>
  public static CheckResult Run(ICheckSource source, string domain) {
    var catalogue = new Dictionary<string, List<Collectible>>(
      StringComparer.Ordinal
    );
    var errors = new List<string>();
    foreach (Construction c in Constructions(source, domain))
      errors.AddRange(Violations(c, source, catalogue));
    return new CheckResult("StageWildcards", domain, errors);
  }

  /// <summary>One block type carrying construction stages.</summary>
  /// <param name="Code">The block type's code, domain-qualified.</param>
  /// <param name="Groups">The names of the block type's own variant groups.</param>
  /// <param name="Stages">The stage table, stage 0 first.</param>
  internal sealed record Construction(
    string Code,
    IReadOnlyList<string> Groups,
    JArray Stages
  );

  /// <summary>Every block type in <paramref name="domain"/> with an
  /// <c>ExRightClickConstructable</c> stage table. A JSON blocktype whose code a definition
  /// declares is not read; definitions that share a code are each read.</summary>
  internal static IEnumerable<Construction> Constructions(
    ICheckSource source,
    string domain
  ) {
    foreach (JObject type in TypesOf(source, domain))
      if (StagesOf(type) is { } stages)
        yield return new Construction(
          $"{domain}:{(string?)type["code"]}",
          [.. GroupsOf(type).Select(g => g.Name)],
          stages
        );
  }

  // A JSON blocktype of a definition's code is that definition's injected asset.
  private static IEnumerable<JObject> TypesOf(
    ICheckSource source,
    string domain
  ) {
    var defined = new HashSet<string>(StringComparer.Ordinal);
    foreach (var def in source.BlockDefinitions(domain))
      if (def.ToJson() is JObject json) {
        defined.Add((string?)json["code"] ?? "");
        yield return json;
      }
    foreach (var (_, json) in source.BlockTypes(domain))
      if (!defined.Contains((string?)json["code"] ?? ""))
        yield return json;
  }

  private static JArray? StagesOf(JObject type) {
    if (type["entityBehaviors"] is not JArray behaviors)
      return null;
    foreach (JToken b in behaviors)
      if (
        (string?)b["name"] == "ExRightClickConstructable"
        && b["properties"]?["stages"] is JArray stages
      )
        return stages;
    return null;
  }

  private static IEnumerable<string> Violations(
    Construction c,
    ICheckSource source,
    Dictionary<string, List<Collectible>> catalogue
  ) {
    HashSet<string> stage1Keys = [.. KeysOf(c.Stages, 1)];
    var storedBefore = new HashSet<string>(StringComparer.Ordinal);
    var firstStore = new Dictionary<string, (int Stage, string Code)>(
      StringComparer.Ordinal
    );
    for (int i = 0; i < c.Stages.Count; i++) {
      foreach (JToken ing in Ingredients(c.Stages, i)) {
        string code = (string?)ing["code"] ?? "";
        string? key = (string?)ing["storeWildCard"];
        string where = $"{c.Code} stage {i} {code}";

        // Stage 0 is refunded but never paid; its placeholders are filled from stage 1's keys.
        IReadOnlySet<string> filled = i == 0 ? stage1Keys : storedBefore;
        foreach (Match m in Placeholder.Matches(code)) {
          string name = m.Groups[1].Value;
          if (!c.Groups.Contains(name) && !filled.Contains(name))
            yield return $"{where}: (e) {{{name}}} is stored by no earlier paid stage";
        }

        if (code.Contains('*')) {
          if (key == null)
            yield return $"{where}: (a) a wildcard without storeWildCard";
          else
            foreach (string line in Spans(code, ing, key, source, catalogue))
              yield return $"{where}: {line}";
        }

        if (key == null)
          continue;
        if (!CreativeKeys.Contains(key))
          yield return $"{where}: (d) key {key} is not seeded by the creative build";
        if (i == 0 && !stage1Keys.Contains(key))
          yield return $"{where}: (f) stage 0 key {key} is not stored by stage 1";
        if (i == 0 || code.Contains("{" + key + "}", StringComparison.Ordinal))
          continue;
        if (!firstStore.TryGetValue(key, out var first))
          firstStore[key] = (i, code);
        else if (first.Stage < i)
          yield return $"{where}: (g) key {key} is stored again; stage {first.Stage} "
            + $"{first.Code} stores it first";
      }
      storedBefore.UnionWith(i == 0 ? [] : KeysOf(c.Stages, i));
    }
  }

  private static IEnumerable<string> Spans(
    string code,
    JToken ing,
    string key,
    ICheckSource source,
    Dictionary<string, List<Collectible>> catalogue
  ) {
    var pattern = new AssetLocation(code);
    if (!source.Domains.Contains(pattern.Domain))
      yield break;
    if (((string?)ing["type"])?.ToLowerInvariant() != "block") {
      yield return "(b, c) undecidable: no item variant groups";
      yield break;
    }

    foreach (
      string line in Decide(
        pattern,
        ing,
        key,
        Catalogue(source, pattern.Domain, catalogue)
      )
    )
      yield return line;
  }

  /// <summary>Rules (b) and (c) for one wildcard ingredient against the concrete collectibles its
  /// pattern is decided among.</summary>
  /// <param name="pattern">The ingredient's code.</param>
  /// <param name="ing">The ingredient, for its <c>allowedVariants</c> and
  /// <c>skipVariants</c>.</param>
  /// <param name="key">The <c>storeWildCard</c> key it stores.</param>
  /// <param name="catalogue">The concrete collectibles of the pattern's domain and item
  /// class.</param>
  internal static IEnumerable<string> Decide(
    AssetLocation pattern,
    JToken ing,
    string key,
    IEnumerable<Collectible> catalogue
  ) {
    string[]? allowed = Strings(ing["allowedVariants"]);
    string[] skipped = Strings(ing["skipVariants"]) ?? [];
    List<Collectible> matches =
    [
      .. catalogue
        .Where(k => WildcardUtil.Match(pattern, new AssetLocation(k.Code)))
        .Where(k =>
          Captured(pattern.Path, new AssetLocation(k.Code).Path) is not { } v
          || ((allowed == null || allowed.Contains(v)) && !skipped.Contains(v))
        ),
    ];
    if (matches.Count == 0) {
      yield return "(c) matches nothing";
      yield break;
    }

    string[] spans =
    [
      .. matches
        .SelectMany(k => k.Variants.Select(v => v.Group))
        .Distinct()
        .Where(g =>
          matches.Any(k => k.Unenumerated.Contains(g))
          || matches
            .Select(k => k.Variants.FirstOrDefault(v => v.Group == g).State)
            .Distinct()
            .Count() > 1
        )
        .OrderBy(g => g, StringComparer.Ordinal),
      .. matches.Select(k => k.Type).Distinct().Count() > 1
        ? matches.Select(k => "code:" + k.Type).Distinct().Order()
        : Enumerable.Empty<string>(),
    ];
    if (spans.Any(s => s != key))
      yield return $"(b) * spans [{string.Join(", ", spans)}], stores {key}";

    foreach (Collectible k in matches)
      if (!k.Variants.Any(v => v.Group == key)) {
        yield return $"(c) {key} is no variant group of {k.Code}";
        yield break;
      }
  }

  // The text a single * captured from path, or null when the pattern holds no single *.
  private static string? Captured(string pattern, string path) {
    int star = pattern.IndexOf('*');
    if (star < 0 || pattern.IndexOf('*', star + 1) >= 0)
      return null;
    string suffix = pattern[(star + 1)..];
    return path.Length >= star + suffix.Length
      ? path[star..(path.Length - suffix.Length)]
      : null;
  }

  /// <summary>One concrete block: its code, its type's code, its variants, and the groups whose
  /// states come from world properties and are not enumerated.</summary>
  internal sealed record Collectible(
    string Code,
    string Type,
    (string Group, string State)[] Variants,
    string[] Unenumerated
  );

  private static List<Collectible> Catalogue(
    ICheckSource source,
    string domain,
    Dictionary<string, List<Collectible>> cache
  ) {
    if (cache.TryGetValue(domain, out List<Collectible>? known))
      return known;
    var all = new List<Collectible>();
    foreach (JObject type in TypesOf(source, domain))
      all.AddRange(Expand(domain, type));
    return cache[domain] = all;
  }

  private static IEnumerable<Collectible> Expand(string domain, JObject type) {
    string baseCode = $"{domain}:{(string?)type["code"]}";
    var groups = GroupsOf(type).ToList();
    string[] unenumerated =
    [
      .. groups.Where(g => g.States == null).Select(g => g.Name),
    ];
    IEnumerable<(string Code, (string, string)[] Variants)> codes =
    [
      (baseCode, []),
    ];
    foreach (var (name, states) in groups)
      codes = codes.SelectMany(c =>
        (states ?? ["*"]).Select(s =>
          (c.Code + "-" + s, c.Variants.Append((name, s)).ToArray())
        )
      );

    string[] skip = Strings(type["skipVariants"]) ?? [];
    string[]? allow = Strings(type["allowedVariants"]);
    foreach (var (code, variants) in codes) {
      var loc = new AssetLocation(code);
      if (skip.Any(s => WildcardUtil.Match(new AssetLocation(domain, s), loc)))
        continue;
      if (
        allow != null
        && !allow.Any(s =>
          WildcardUtil.Match(new AssetLocation(domain, s), loc)
        )
      )
        continue;
      yield return new Collectible(code, baseCode, variants, unenumerated);
    }
  }

  // A worldproperty group has no states here, and takes its name from the property's last segment
  // when it declares no code.
  private static IEnumerable<(string Name, string[]? States)> GroupsOf(
    JObject type
  ) {
    if (type["variantgroups"] is not JArray groups)
      yield break;
    foreach (JToken g in groups) {
      string? props = (string?)g["loadFromProperties"];
      string name = (string?)g["code"] ?? props?.Split('/').Last() ?? "";
      if (name.Length > 0)
        yield return (name, Strings(g["states"]));
    }
  }

  /// <summary>The <c>requireStacks</c> of stage <paramref name="i"/>; empty past the last
  /// stage.</summary>
  internal static IEnumerable<JToken> Ingredients(JArray stages, int i) =>
    i < stages.Count && stages[i]["requireStacks"] is JArray stacks
      ? stacks
      : [];

  private static IEnumerable<string> KeysOf(JArray stages, int i) =>
    Ingredients(stages, i)
      .Select(ing => (string?)ing["storeWildCard"])
      .OfType<string>();

  private static string[]? Strings(JToken? token) =>
    token is JArray arr ? [.. arr.Select(t => (string)t!)] : null;
}
