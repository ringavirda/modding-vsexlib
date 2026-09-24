using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every <c>game:</c> code a domain's recipes, construction stages and definition
/// bodies point at names a block or item the loaded game registered.
/// </summary>
/// <remarks>A code is read with its placeholders filled: a recipe's <c>{name}</c> by the states
/// its ingredients' <c>allowedVariants</c> bind, a definition's by its own variant groups and the
/// keys its construction stages store; a placeholder nothing binds is filled at run time and reads
/// as <c>*</c>. A wildcard resolves when it matches one loaded code. A code without a domain names
/// <c>game</c> in a construction stage, which is read at run time, and its own file's domain
/// elsewhere, as the game's loader reads it.</remarks>
public static class GameReferencesCheck {
  /// <summary>Every <c>game:</c> reference in <paramref name="domain"/> that names nothing
  /// loaded.</summary>
  /// <param name="game">The loaded game and the domain's recipes and definitions.</param>
  /// <param name="domain">The domain whose references are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>GameReferences</c>, one error per
  /// distinct unresolved reference; no errors when none.</returns>
  public static CheckResult Run(ILoadedGame game, string domain) {
    var codes = new Dictionary<bool, AssetLocation[]> {
      [true] = [],
      [false] = [],
    };
    foreach (
      IGrouping<bool, CollectibleObject> kind in game.Collectibles.GroupBy(c =>
        c.ItemClass == EnumItemClass.Block
      )
    )
      codes[kind.Key] = [.. kind.Select(c => c.Code)];
    var exact = codes.ToDictionary(
      kv => kv.Key,
      kv => kv.Value.Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal)
    );

    var errors = new List<string>();
    foreach (
      Reference r in InRecipes(game, domain)
        .Concat(InDefinitions(game, domain))
        .Select(r => r with { Code = r.Qualified })
        .Where(r => r.Code.StartsWith("game:", StringComparison.Ordinal))
        .Distinct()
    )
      if (!Resolves(r, codes[r.IsBlock], exact[r.IsBlock]))
        errors.Add($"{r}: names nothing the game loaded");
    return new CheckResult("GameReferences", domain, errors);
  }

  private static bool Resolves(
    Reference reference,
    AssetLocation[] codes,
    HashSet<string> exact
  ) {
    if (exact.Contains(reference.Code))
      return true;
    string code = Unbound(reference.Code);
    if (!code.Contains('*') && !code.Contains('@'))
      return false;
    var pattern = new AssetLocation(code);
    int wild = pattern.Path.IndexOfAny(['*', '@', '(']);
    string prefix = wild < 0 ? pattern.Path : pattern.Path[..wild];
    return codes.Any(c =>
      c.Domain == pattern.Domain
      && c.Path.StartsWith(prefix, StringComparison.Ordinal)
      && WildcardUtil.Match(pattern, c)
    );
  }

  /// <summary><paramref name="code"/> with each <c>{name}</c> placeholder left in it read as
  /// <c>*</c>.</summary>
  internal static string Unbound(string code) =>
    System.Text.RegularExpressions.Regex.Replace(code, @"\{[^}]*\}", "*");

  /// <summary>Where a reference was authored.</summary>
  internal enum Origin {
    RecipeOutput,
    RecipeIngredient,
    ConstructionRequire,
    DefinitionStack,
  }

  /// <summary>One authored reference, with its placeholders filled.</summary>
  /// <param name="Source">The file that authored it, short form.</param>
  /// <param name="Origin">Where in the file.</param>
  /// <param name="Code">The concrete or wildcard code.</param>
  /// <param name="IsBlock">Which registry it lands in; <c>type</c> decides.</param>
  internal sealed record Reference(
    string Source,
    Origin Origin,
    string Code,
    bool IsBlock
  ) {
    /// <summary>The domain segment of <see cref="Code"/>, or <c>game</c> when it carries
    /// none.</summary>
    internal string Domain =>
      Code.Contains(':', StringComparison.Ordinal)
        ? Code[..Code.IndexOf(':', StringComparison.Ordinal)]
        : GlobalConstants.DefaultDomain;

    /// <summary><see cref="Code"/> with a domain: a bare code names <c>game</c> in a construction
    /// stage and <see cref="Source"/>'s domain elsewhere.</summary>
    internal string Qualified =>
      Code.Contains(':', StringComparison.Ordinal) ? Code
      : Origin == Origin.ConstructionRequire ? "game:" + Code
      : Source.Contains(':', StringComparison.Ordinal)
        ? Source[..(Source.IndexOf(':', StringComparison.Ordinal) + 1)] + Code
      : "game:" + Code;

    /// <inheritdoc/>
    public override string ToString() =>
      $"{Source}: {Code} ({(IsBlock ? "block" : "item")}, {Origin})";
  }

  #region Collecting

  /// <summary>Every code <paramref name="domain"/>'s recipes reference: outputs and ingredients of
  /// all three recipe shapes.</summary>
  internal static IEnumerable<Reference> InRecipes(
    ICheckSource source,
    string domain
  ) {
    foreach ((AssetLocation file, JObject recipe) in source.Recipes(domain)) {
      string at = file.ToShortString();
      Dictionary<string, string[]> holes = RecipeHoles(recipe);

      foreach (Reference r in Read(recipe["output"], at, Origin.RecipeOutput, holes))
        yield return r;

      // Grid recipes key ingredients by pattern letter, barrel recipes list them, smithing has one.
      IEnumerable<JToken> ingredients = recipe["ingredients"] switch {
        JObject slots => slots.Properties().Select(p => p.Value),
        JArray list => list,
        _ => [],
      };
      IEnumerable<JToken> allIngredients = recipe["ingredient"] is JToken single
        ? ingredients.Append(single)
        : ingredients;
      foreach (JToken ingredient in allIngredients)
        foreach (
          Reference r in Read(ingredient, at, Origin.RecipeIngredient, holes)
        )
          yield return r;
    }
  }

  /// <summary>Every code <paramref name="domain"/>'s blocktypes and itemtypes name in their own
  /// bodies: construction requires, drops, smelted, ground and shattered stacks, mold outputs. A
  /// JSON blocktype of a code a block definition declares is that definition's and not read
  /// twice.</summary>
  internal static IEnumerable<Reference> InDefinitions(
    ICheckSource source,
    string domain
  ) {
    var defined = new HashSet<string>(StringComparer.Ordinal);
    var types = new List<(string Source, JToken Json)>();
    foreach (var def in source.BlockDefinitions(domain)) {
      JToken json = def.ToJson();
      defined.Add((string?)json["code"] ?? "");
      types.Add((def.Location.ToShortString(), json));
    }
    foreach ((AssetLocation file, JObject json) in source.BlockTypes(domain))
      if (!defined.Contains((string?)json["code"] ?? ""))
        types.Add((file.ToShortString(), json));
    foreach ((AssetLocation file, JObject json) in source.ItemTypes(domain))
      types.Add((file.ToShortString(), json));

    foreach ((string at, JToken json) in types) {
      Dictionary<string, string[]> holes = VariantStates(json);
      foreach ((string name, string[] states) in ConstructionWildCards(json))
        holes[name] = states;

      foreach (Reference r in Stacks(json, at, holes, false))
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
  /// property binds to <c>*</c>.</summary>
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
        (string?)b["name"] == "ExRightClickConstructable"
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
}
