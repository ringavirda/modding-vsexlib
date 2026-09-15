using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>Collects every code a mod's recipes, construction stages and definition bodies point at,
/// as opposed to the codes they register, and reports the ones that name nothing.</summary>
public static class ReferencedCodes {
  /// <summary>Where a reference was authored, which is what a failure message has to name for the
  /// reference to be findable.</summary>
  public enum Origin {
    /// <summary>A recipe's <c>output</c>.</summary>
    RecipeOutput,

    /// <summary>A recipe ingredient, from any of the three shapes (grid slot map, smithing singular,
    /// barrel array).</summary>
    RecipeIngredient,

    /// <summary>One <c>requireStacks</c> entry of an <c>ExRightClickConstructable</c> stage.</summary>
    ConstructionRequire,

    /// <summary>A stack a blocktype or itemtype names in its own body: a drop, a smelted, ground or
    /// shattered stack, a mold's output.</summary>
    DefinitionStack,
  }

  /// <summary>One authored reference, with its placeholders already filled.</summary>
  /// <param name="Source">The def that authored it - a recipe's asset path, or a block code.</param>
  /// <param name="Code">The concrete (or wildcard) code, after placeholder expansion.</param>
  /// <param name="IsBlock">Which registry it lands in; <c>type</c> decides, defaulting to item as the
  /// game's own deserializer does.</param>
  public sealed record Reference(
    string Source,
    Origin Origin,
    string Code,
    bool IsBlock
  ) {
    /// <summary>The domain segment of <see cref="Code"/>, or <c>game</c> when it carries none.</summary>
    public string Domain =>
      Code.Contains(':', StringComparison.Ordinal)
        ? Code[..Code.IndexOf(':', StringComparison.Ordinal)]
        : GlobalConstants.DefaultDomain;

    /// <inheritdoc/>
    public override string ToString() =>
      $"{Source}: {Code} ({(IsBlock ? "block" : "item")}, {Origin})";
  }

  #region Collecting

  /// <summary>Every code <paramref name="domain"/>'s recipe files reference - outputs and ingredients of
  /// all three recipe shapes.</summary>
  public static IEnumerable<Reference> InRecipes(string domain, Assembly asm) {
    foreach (
      ExRecipeDef def in DefinitionGoldens
        .Collect(domain, asm)
        .OfType<ExRecipeDef>()
    ) {
      string source = def.Location.ToShortString();
      JToken json = def.ToJson();
      foreach (JToken recipe in json is JArray arr ? arr : [json]) {
        Dictionary<string, string[]> holes = RecipeHoles(recipe);

        foreach (
          Reference r in Read(
            recipe["output"],
            source,
            Origin.RecipeOutput,
            holes
          )
        )
          yield return r;

        // Grid recipes key ingredients by pattern letter, barrel recipes list them, smithing has one.
        IEnumerable<JToken> ingredients = recipe["ingredients"] switch {
          JObject slots => slots.Properties().Select(p => p.Value),
          JArray list => list,
          _ => [],
        };
        IEnumerable<JToken> allIngredients = recipe["ingredient"]
          is JToken single
          ? ingredients.Append(single)
          : ingredients;
        foreach (JToken ingredient in allIngredients)
          foreach (
            Reference r in Read(
              ingredient,
              source,
              Origin.RecipeIngredient,
              holes
            )
          )
            yield return r;
      }
    }
  }

  /// <summary>Every code <paramref name="domain"/>'s blocktypes and itemtypes name in their own
  /// bodies: construction requires, drops, smelted, ground and shattered stacks, mold outputs.</summary>
  public static IEnumerable<Reference> InDefinitions(
    string domain,
    Assembly asm
  ) {
    foreach (IExDef def in DefinitionGoldens.Collect(domain, asm)) {
      if (def is not (ExBlockDef or ExItemDef))
        continue;

      JToken json = def.ToJson();
      string source = def.Location.ToShortString();
      Dictionary<string, string[]> holes = VariantStates(json);
      foreach ((string name, string[] states) in ConstructionWildCards(json))
        holes[name] = states;

      foreach (Reference r in Stacks(json, source, holes, false))
        yield return r;
    }
  }

  // Depth-first; `inRequire` tracks whether the subtree sits under a requireStacks array.
  private static IEnumerable<Reference> Stacks(
    JToken node,
    string source,
    Dictionary<string, string[]> holes,
    bool inRequire
  ) {
    if (node is JObject obj) {
      foreach (
        Reference r in Read(
          obj,
          source,
          inRequire ? Origin.ConstructionRequire : Origin.DefinitionStack,
          holes
        )
      )
        yield return r;

      foreach (JProperty property in obj.Properties())
        foreach (
          Reference r in Stacks(
            property.Value,
            source,
            holes,
            inRequire || property.Name == "requireStacks"
          )
        )
          yield return r;
    } else if (node is JArray array) {
      foreach (JToken child in array)
        foreach (Reference r in Stacks(child, source, holes, inRequire))
          yield return r;
    }
  }

  // One stack object into references, one per state its placeholders can take.
  private static IEnumerable<Reference> Read(
    JToken? stack,
    string source,
    Origin origin,
    Dictionary<string, string[]> holes
  ) {
    if (stack is not JObject obj || (string?)obj["code"] is not { } code)
      return [];

    bool isBlock = (string?)obj["type"] == "block";
    if (!isBlock && (string?)obj["type"] != "item")
      return [];

    return Fill(code, holes)
      .Select(c => new Reference(source, origin, c, isBlock));
  }

  #endregion

  #region Placeholders

  /// <summary>The <c>{name}</c> holes a recipe's codes can carry, mapped to the states they may take,
  /// as bound by an ingredient's <c>allowedVariants</c>.</summary>
  private static Dictionary<string, string[]> RecipeHoles(JToken recipe) {
    var holes = new Dictionary<string, string[]>(StringComparer.Ordinal);
    IEnumerable<JToken> ingredients = recipe["ingredients"] switch {
      JObject slots => slots.Properties().Select(p => p.Value),
      JArray list => list,
      _ => [],
    };

    foreach (JToken? ingredient in ingredients.Append(recipe["ingredient"]))
      Bind(holes, ingredient, "name");
    return holes;
  }

  /// <summary>The states a definition's own variant groups can take. A group sourced from a world
  /// property binds to <c>*</c>, matching <see cref="DefinitionCodes.Expand"/>.</summary>
  private static Dictionary<string, string[]> VariantStates(JToken definition) {
    var holes = new Dictionary<string, string[]>(StringComparer.Ordinal);
    if (definition["variantgroups"] is not JArray groups)
      return holes;

    foreach (JToken group in groups) {
      string? name =
        (string?)group["code"]
        ?? ((string?)group["loadFromProperties"])?.Split('/').Last();
      if (string.IsNullOrEmpty(name))
        continue;
      holes[name] = group["states"] is JArray states
        ? [.. states.Select(s => (string)s!)]
        : ["*"];
    }
    return holes;
  }

  /// <summary>The wildcards a definition's construction stages store for later stages to fill via
  /// <c>storeWildCard</c>.</summary>
  private static Dictionary<string, string[]> ConstructionWildCards(
    JToken definition
  ) {
    var holes = new Dictionary<string, string[]>(StringComparer.Ordinal);
    if (
      definition["entityBehaviors"] is not JArray behaviors
      || behaviors.FirstOrDefault(b =>
        (string?)b["name"] == nameof(ExRightClickConstructable)
      )
        is not { } rcc
      || rcc["properties"]?["stages"] is not JArray stages
    )
      return holes;

    foreach (JToken stage in stages)
      if (stage["requireStacks"] is JArray required)
        foreach (JToken ingredient in required)
          Bind(holes, ingredient, "storeWildCard");
    return holes;
  }

  // Records the states one ingredient binds under the name it declares at nameKey; repeats union.
  private static void Bind(
    Dictionary<string, string[]> holes,
    JToken? ingredient,
    string nameKey
  ) {
    if (
      ingredient?[nameKey] is not { } name
      || ingredient["allowedVariants"] is not JArray states
    )
      return;

    string key = (string)name!;
    string[] declared = [.. states.Select(s => (string)s!)];
    holes[key] = holes.TryGetValue(key, out string[]? seen)
      ? [.. seen.Union(declared)]
      : declared;
  }

  // Every concrete code the holes expand this one to. A hole with no binding stays written.
  private static IEnumerable<string> Fill(
    string code,
    Dictionary<string, string[]> holes
  ) {
    IEnumerable<string> codes = [code];
    foreach ((string name, string[] states) in holes) {
      string hole = "{" + name + "}";
      codes = codes.SelectMany(c =>
        c.Contains(hole, StringComparison.Ordinal)
          ? states.Select(s => c.Replace(hole, s, StringComparison.Ordinal))
          : [c]
      );
    }
    return codes;
  }

  #endregion

  #region Resolving

  /// <summary>The subset of <paramref name="references"/> that name nothing any of
  /// <paramref name="domains"/> registers.</summary>
  public static IReadOnlyList<Reference> Unresolvable(
    IEnumerable<Reference> references,
    IReadOnlyDictionary<string, Assembly> domains
  ) {
    var catalogue = new Catalogue(domains);
    return [.. references.Where(r => !catalogue.Resolves(r)).Distinct()];
  }

  /// <summary>How many of <paramref name="references"/> this harness can actually judge: the ones
  /// whose domain is in <paramref name="domains"/>.</summary>
  public static int Checkable(
    IEnumerable<Reference> references,
    IReadOnlyDictionary<string, Assembly> domains
  ) => references.Count(r => domains.ContainsKey(r.Domain));

  /// <summary>References written with no domain whose exact path names something a mod registers.
  /// Wildcards are excluded.</summary>
  public static IReadOnlyList<Reference> BareButOurs(
    IEnumerable<Reference> references,
    IReadOnlyDictionary<string, Assembly> domains
  ) {
    var catalogue = new Catalogue(domains);
    return
    [
      .. references
        .Where(r =>
          !r.Code.Contains(':', StringComparison.Ordinal)
          && !r.Code.Contains('*', StringComparison.Ordinal)
          && !r.Code.Contains("@(", StringComparison.Ordinal)
        )
        .Where(r =>
          domains.Keys.Any(d =>
            catalogue.Resolves(r with { Code = $"{d}:{r.Code}" })
          )
        )
        .Distinct(),
    ];
  }

  // Registered code patterns per domain, expanded once and cached.
  private sealed class Catalogue(IReadOnlyDictionary<string, Assembly> domains) {
    private readonly Dictionary<
      (string Domain, bool IsBlock),
      AssetLocation[]
    > _cache = [];

    internal bool Resolves(Reference reference) {
      if (!domains.TryGetValue(reference.Domain, out Assembly? asm))
        return true;

      var target = new AssetLocation(reference.Code);
      // Both directions: either side may carry a wildcard, and WildcardUtil only reads the pattern side.
      return Patterns(reference.Domain, reference.IsBlock, asm)
        .Any(p =>
          WildcardUtil.Match(p, target) || WildcardUtil.Match(target, p)
        );
    }

    private AssetLocation[] Patterns(string domain, bool isBlock, Assembly asm) {
      if (_cache.TryGetValue((domain, isBlock), out AssetLocation[]? cached))
        return cached;

      AssetLocation[] patterns =
      [
        .. (
          isBlock
            ? DefinitionCatalogue.BlockPatterns(domain, asm)
            : DefinitionCatalogue.ItemPatterns(domain, asm)
        ).Select(c => new AssetLocation(c)),
      ];
      _cache[(domain, isBlock)] = patterns;
      return patterns;
    }
  }

  #endregion
}
