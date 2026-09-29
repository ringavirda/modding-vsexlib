using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every recipe's block output in the mod's own domain names a block the mod
/// registers, matched against <see cref="ICheckSource.BlockCodes"/>. Item outputs are not covered.
/// </summary>
public static class RecipeCodesCheck {
  /// <summary>Every recipe output in <paramref name="domain"/> that names no registered block, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) =>
    new(
      "RecipeCodes",
      domain,
      [
        .. Unresolvable(source, domain)
          .Select(o => $"{o.File.ToShortString()}: {o.Code}"),
      ]
    );

  // An output holding a wildcard is reported; a {name} a named ingredient without allowedVariants
  // binds resolves when some registered code matches it in any state.
  internal static IEnumerable<(AssetLocation File, string Code)> Unresolvable(
    ICheckSource source,
    string domain
  ) {
    AssetLocation[] registered = [.. source.BlockCodes];
    foreach (
      (AssetLocation file, JObject recipe, string code) in Candidates(
        source,
        domain
      )
    ) {
      string open = OpenNames(recipe)
        .Aggregate(
          code,
          (c, name) =>
            c.Replace("{" + name + "}", "*", StringComparison.Ordinal)
        );
      bool resolves =
        open == code
          ? registered.Any(c => WildcardUtil.Match(c, new AssetLocation(code)))
          : registered.Any(c => WildcardUtil.Match(new AssetLocation(open), c));
      if (!resolves)
        yield return (file, code);
    }
  }

  // Every concrete block code in the domain's own namespace that a recipe outputs, with its
  // placeholders expanded, paired with the file the recipe sits in.
  internal static IEnumerable<(AssetLocation File, string Code)> Outputs(
    ICheckSource source,
    string domain
  ) => Candidates(source, domain).Select(c => (c.File, c.Code));

  private static IEnumerable<(
    AssetLocation File,
    JObject Recipe,
    string Code
  )> Candidates(ICheckSource source, string domain) {
    foreach ((AssetLocation file, JObject recipe) in source.Recipes(domain)) {
      if (recipe["output"] is not JObject output)
        continue;
      if (
        (string?)output["type"] != "block"
        || (string?)output["code"] is not { } code
      )
        continue;
      if (!code.StartsWith(domain + ":", StringComparison.Ordinal))
        continue;

      foreach (string concrete in Expand(code, Placeholders(recipe)))
        yield return (file, recipe, concrete);
    }
  }

  // The names of the recipe's named ingredients that carry no allowedVariants.
  private static IEnumerable<string> OpenNames(JObject recipe) =>
    Ingredients(recipe)
      .Where(i => i["name"] != null && i["allowedVariants"] is not JArray)
      .Select(i => (string)i["name"]!);

  // The {name} holes a recipe's output can carry, mapped to the states an ingredient binds them to.
  internal static Dictionary<string, string[]> Placeholders(JObject recipe) {
    var holes = new Dictionary<string, string[]>(StringComparer.Ordinal);
    foreach (JObject ingredient in Ingredients(recipe)) {
      if (
        ingredient["name"] is not { } name
        || ingredient["allowedVariants"] is not JArray states
      )
        continue;
      holes[(string)name!] = [.. states.Select(s => (string)s!)];
    }
    return holes;
  }

  // A grid recipe keys its ingredients by pattern letter; a clayforming, knapping or smithing
  // recipe lists them or names a single ingredient.
  private static IEnumerable<JObject> Ingredients(JObject recipe) {
    IEnumerable<JToken> all = recipe["ingredients"] switch {
      JObject keyed => keyed.Properties().Select(p => p.Value),
      JArray listed => listed,
      _ => recipe["ingredient"] is { } single ? [single] : [],
    };
    return all.OfType<JObject>();
  }

  internal static IEnumerable<string> Expand(
    string code,
    Dictionary<string, string[]> holes
  ) {
    IEnumerable<string> codes = [code];
    foreach (var (name, states) in holes) {
      string hole = "{" + name + "}";
      codes = codes.SelectMany(c =>
        c.Contains(hole, StringComparison.Ordinal)
          ? states.Select(s => c.Replace(hole, s, StringComparison.Ordinal))
          : [c]
      );
    }
    return codes;
  }
}
