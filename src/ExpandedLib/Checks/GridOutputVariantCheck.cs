using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using static ExpandedLib.Checks.GridRecipeShapeCheck;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that a grid recipe whose output names an oriented block names the orientation its
/// blocktype's <c>creativeinventory</c> lists, the block's creative default.
/// </summary>
/// <remarks>
/// A block is oriented by the variant group its orientation behaviour writes when placed:
/// <c>ExOrientable</c>'s <c>side</c>, or <c>orientation</c> in <c>network</c> mode
/// (<c>BlockBehaviorExOrientable.VariantKey</c>); vanilla's <c>HorizontalOrientable</c>'s
/// <c>horizontalorientation</c> and <c>NWOrientable</c>'s <c>orientation</c>, each <c>side</c>
/// when the block declares no such group; <c>Pillar</c>'s <c>rotationVariantCode</c>, by default
/// <c>rotation</c>; and <c>OmniRotatable</c>'s <c>rot</c>. An output is reported when no
/// <c>creativeinventory</c> entry matches it and one matches it with only that group's state
/// changed; an output differing from the default in any other group is not this check's. A
/// block without a <c>creativeinventory</c>, or whose groups combine other than by multiplying,
/// is not read. Blocktypes are read from every domain the source covers. An output code without a
/// domain is in its recipe file's domain.
/// </remarks>
public static class GridOutputVariantCheck {
  private static readonly Regex Placeholder = new(@"\{[^}]*\}");

  /// <summary>Every grid recipe output in <paramref name="domain"/> that names an oriented block
  /// in an orientation other than its creative default.</summary>
  /// <param name="source">The recipes, block definitions and JSON blocktypes to read.</param>
  /// <param name="domain">The domain whose grid recipes are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>GridOutputVariant</c>, one error
  /// per such output, naming the default; no errors when none.</returns>
  public static CheckResult Run(ICheckSource source, string domain) {
    var types = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
    var errors = new List<string>();
    foreach (GridEntry recipe in Recipes(source, domain)) {
      if (
        Prop(recipe.Json, "output") is not JObject output
        || (string?)Prop(output, "code") is not { } code
        || ((string?)Prop(output, "type") ?? "block").ToLowerInvariant()
          != "block"
      )
        continue;
      foreach (
        string expanded in RecipeCodesCheck.Expand(
          code,
          RecipeCodesCheck.Placeholders(recipe.Json)
        )
      ) {
        var location = AssetLocation.Create(
          Placeholder.Replace(expanded, "*"),
          domain
        );
        if (!source.Domains.Append(domain).Contains(location.Domain))
          continue;
        if (!types.TryGetValue(location.Domain, out List<JObject>? declared))
          types[location.Domain] = declared = [
            .. TypesOf(source, location.Domain),
          ];
        foreach (JObject type in declared)
          if (Default(type, location.Path) is { } fallback)
            errors.Add(
              $"{recipe.Where}: output {location} is not the creative default "
                + $"{location.Domain}:{fallback}"
            );
      }
    }
    return new CheckResult("GridOutputVariant", domain, errors);
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

  // The creative default of path, when path names an oriented variant of type the creative
  // inventory does not list; null otherwise.
  private static string? Default(JObject type, string path) {
    JObject tabs = Prop(type, "creativeinventory") as JObject ?? [];
    List<(string Name, string[]? States)> groups = [.. Groups(type)];
    if (groups.Count == 0 || Parse(type, groups, path) is not { } states)
      return null;
    string[] listed =
    [
      .. tabs.Properties()
        .SelectMany(t => t.Value as JArray ?? [])
        .Select(p => (string?)p ?? ""),
    ];
    if (Listed(listed, groups, CodeOf(type), states))
      return null;

    foreach (string oriented in OrientationGroups(type, groups)) {
      int at = groups.FindIndex(g => g.Name == oriented);
      foreach (string state in groups[at].States ?? []) {
        string[] turned = [.. states];
        turned[at] = state;
        if (Listed(listed, groups, CodeOf(type), turned))
          return $"{CodeOf(type)}-{string.Join("-", turned)}";
      }
    }
    return null;
  }

  // BlockType.GetCreativeTabs: each entry, its {group} placeholders filled, matched against the
  // code's path.
  private static bool Listed(
    string[] listed,
    List<(string Name, string[]? States)> groups,
    string code,
    string[] states
  ) {
    string path = $"{code}-{string.Join("-", states)}";
    return listed.Any(entry => {
      for (int i = 0; i < groups.Count; i++)
        entry = entry.Replace(
          "{" + groups[i].Name + "}",
          states[i],
          StringComparison.Ordinal
        );
      return WildcardUtil.Match(entry, path);
    });
  }

  // Null states for a group loaded from world properties; none when a group does not multiply.
  private static IEnumerable<(string Name, string[]? States)> Groups(
    JObject type
  ) {
    if (Prop(type, "variantgroups") is not JArray groups)
      return [];
    var read = new List<(string, string[]?)>();
    foreach (JObject group in groups.OfType<JObject>()) {
      string combine = (string?)Prop(group, "combine") ?? "multiply";
      if (!combine.Equals("multiply", StringComparison.OrdinalIgnoreCase))
        return [];
      read.Add(
        (
          (string?)Prop(group, "code") ?? "",
          Prop(group, "states") is JArray states
            ? [.. states.Select(s => (string)s!)]
            : null
        )
      );
    }
    return read;
  }

  private static string CodeOf(JObject type) {
    string code = (string?)Prop(type, "code") ?? "";
    return code.Contains(':') ? code[(code.IndexOf(':') + 1)..] : code;
  }

  // The state path gives each group, or null when path is no variant of type. An unexpanded
  // output placeholder, written *, stands for any state.
  private static string[]? Parse(
    JObject type,
    List<(string Name, string[]? States)> groups,
    string path
  ) {
    string pattern =
      "^"
      + Regex.Escape(CodeOf(type))
      + string.Concat(
        groups.Select(g =>
          "-("
          + (
            g.States is { } states
              ? string.Join("|", states.Select(Regex.Escape).Append(@"\*"))
              : "[^-]+"
          )
          + ")"
        )
      )
      + "$";
    Match match = Regex.Match(path, pattern);
    return match.Success
      ? [.. match.Groups.Cast<Group>().Skip(1).Select(g => g.Value)]
      : null;
  }

  // The groups the type's orientation behaviours write, among those it declares with states.
  private static IEnumerable<string> OrientationGroups(
    JObject type,
    List<(string Name, string[]? States)> groups
  ) {
    bool Has(string name) =>
      groups.Any(g => g.Name == name && g.States != null);
    if (Prop(type, "behaviors") is not JArray behaviors)
      yield break;
    foreach (JObject behavior in behaviors.OfType<JObject>()) {
      JObject? properties = Prop(behavior, "properties") as JObject;
      string? group = ((string?)Prop(behavior, "name")) switch {
        "ExOrientable" => (string?)properties?["mode"] == "network"
          ? BlockBehaviorExOrientable.OrientationVariant
          : BlockBehaviorExOrientable.SideVariant,
        "HorizontalOrientable" => Has("horizontalorientation")
          ? "horizontalorientation"
          : "side",
        "NWOrientable" => Has("orientation") ? "orientation" : "side",
        "Pillar" => (string?)properties?["rotationVariantCode"] ?? "rotation",
        "OmniRotatable" => "rot",
        _ => null,
      };
      if (group != null && Has(group))
        yield return group;
    }
  }
}
