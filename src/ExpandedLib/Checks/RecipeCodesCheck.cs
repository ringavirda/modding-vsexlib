using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every grid recipe's block output in the mod's own domain names a block the mod
/// registers, matched against <see cref="ICheckSource.BlockCodes"/>. Item outputs are not covered.
/// </summary>
public static class RecipeCodesCheck {
  /// <summary>Every recipe output in <paramref name="domain"/> that names no registered block, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) {
    AssetLocation[] registered = [.. source.BlockCodes];

    var errors = new List<string>();
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

      foreach (string concrete in Expand(code, Placeholders(recipe))) {
        var target = new AssetLocation(concrete);
        if (
          !registered.Any(c =>
            WildcardUtil.Match(c, target) || WildcardUtil.Match(target, c)
          )
        )
          errors.Add($"{file.ToShortString()}: {concrete}");
      }
    }
    return new CheckResult("RecipeCodes", domain, errors);
  }

  // The {name} holes a recipe's output can carry, mapped to the states an ingredient binds them to.
  private static Dictionary<string, string[]> Placeholders(JObject recipe) {
    var holes = new Dictionary<string, string[]>(StringComparer.Ordinal);
    if (recipe["ingredients"] is not JObject ingredients)
      return holes;

    foreach (JProperty slot in ingredients.Properties()) {
      if (
        slot.Value["name"] is not { } name
        || slot.Value["allowedVariants"] is not JArray states
      )
        continue;
      holes[(string)name!] = [.. states.Select(s => (string)s!)];
    }
    return holes;
  }

  private static IEnumerable<string> Expand(
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
