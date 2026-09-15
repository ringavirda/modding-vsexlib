using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>Checks that every grid recipe's block output names a block the mod registers, scoped to
/// block outputs in the mod's own domain.</summary>
public static class RecipeCodes {
  /// <summary>One output that names no registered block: the recipe file it sits in and the code.</summary>
  public sealed record Unresolvable(string RecipePath, string Code);

  /// <summary>Every concrete block code <paramref name="domain"/>'s grid recipes can output, with
  /// placeholders expanded.</summary>
  public static IEnumerable<string> OutputBlockCodes(
    string domain,
    Assembly asm
  ) {
    foreach (
      ExRecipeDef def in DefinitionGoldens
        .Collect(domain, asm)
        .OfType<ExRecipeDef>()
    ) {
      JToken json = def.ToJson();
      IEnumerable<JToken> recipes = json is JArray arr ? arr : [json];
      foreach (JToken recipe in recipes) {
        if (recipe["output"] is not JObject output)
          continue;
        if (
          (string?)output["type"] != "block"
          || (string?)output["code"] is not { } code
        )
          continue;
        if (!code.StartsWith(domain + ":", System.StringComparison.Ordinal))
          continue;

        foreach (string concrete in Expand(code, Placeholders(recipe)))
          yield return concrete;
      }
    }
  }

  /// <summary>Every block output in <paramref name="domain"/>'s recipes that no definition in the same
  /// assembly produces.</summary>
  public static IReadOnlyList<Unresolvable> UnresolvableOutputs(
    string domain,
    Assembly asm
  ) {
    // Patterns, not codes: membership is a wildcard match rather than a set lookup.
    AssetLocation[] registered =
    [
      .. DefinitionCodes
        .PatternsForDomain(domain, asm)
        .Select(c => new AssetLocation(c)),
    ];

    var bad = new List<Unresolvable>();
    foreach (
      ExRecipeDef def in DefinitionGoldens
        .Collect(domain, asm)
        .OfType<ExRecipeDef>()
    ) {
      JToken json = def.ToJson();
      IEnumerable<JToken> recipes = json is JArray arr ? arr : [json];
      foreach (JToken recipe in recipes) {
        if (recipe["output"] is not JObject output)
          continue;
        if (
          (string?)output["type"] != "block"
          || (string?)output["code"] is not { } code
        )
          continue;
        if (!code.StartsWith(domain + ":", System.StringComparison.Ordinal))
          continue;

        foreach (string concrete in Expand(code, Placeholders(recipe))) {
          var target = new AssetLocation(concrete);
          if (!registered.Any(p => WildcardUtil.Match(p, target)))
            bad.Add(new Unresolvable(def.Location.ToShortString(), concrete));
        }
      }
    }
    return bad;
  }

  /// <summary>The <c>{name}</c> holes a recipe's output can carry, mapped to the states they may take,
  /// as bound by the ingredients' <c>allowedVariants</c>.</summary>
  private static Dictionary<string, string[]> Placeholders(JToken recipe) {
    var holes = new Dictionary<string, string[]>(System.StringComparer.Ordinal);
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

  /// <summary>Every concrete code <paramref name="code"/> expands to once its holes are filled.</summary>
  private static IEnumerable<string> Expand(
    string code,
    Dictionary<string, string[]> holes
  ) {
    IEnumerable<string> codes = [code];
    foreach (var (name, states) in holes) {
      string hole = "{" + name + "}";
      codes = codes.SelectMany(c =>
        c.Contains(hole, System.StringComparison.Ordinal)
          ? states.Select(s =>
            c.Replace(hole, s, System.StringComparison.Ordinal)
          )
          : [c]
      );
    }
    return codes;
  }
}
