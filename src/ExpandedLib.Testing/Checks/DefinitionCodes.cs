using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Expands code-first block definitions into the concrete block codes they register.
/// </summary>
public static class DefinitionCodes {
  /// <summary>States for a variant group the game owns; sampled, not enumerated.
  /// <c>horizontalorientation</c> is exact (four); other groups take one representative.</summary>
  private static readonly Dictionary<string, string[]> PropertySamples = new() {
    ["horizontalorientation"] = ["north", "east", "south", "west"],
    ["rockwithdeposit"] = ["granite"],
    ["rock"] = ["granite"],
  };

  /// <summary>One concrete registered block: its domain-qualified code and the variant map that
  /// produced it.</summary>
  public sealed record Registered(
    string Code,
    (string Key, string Value)[] Variants
  );

  /// <summary>Every concrete block <paramref name="def"/> registers, with its variant map.</summary>
  /// <param name="propertyGroupsAsWildcard">
  /// Renders a worldproperty-sourced group as a literal <c>*</c>, for matching a code against the
  /// pattern, not enumerating concrete states.
  /// </param>
  public static IEnumerable<Registered> Expand(
    ExBlockDef def,
    bool propertyGroupsAsWildcard = false
  ) {
    var groups = new List<(string Name, string[] States)>();
    if (def.ToJson()["variantgroups"] is JArray vg)
      foreach (JToken g in vg) {
        string? props = (string?)g["loadFromProperties"];
        // A codeless worldproperty group takes its name from the property's last segment, which is
        // vanilla's form for horizontal orientation.
        string name = (string?)g["code"] ?? props?.Split('/').Last() ?? "";
        if (name.Length == 0)
          continue;

        string[] states =
          g["states"] is JArray arr ? arr.Select(s => (string)s!).ToArray()
          : propertyGroupsAsWildcard ? ["*"]
          : PropertySamples.GetValueOrDefault(
            (props ?? "").Split('/').Last(),
            ["north"]
          );
        groups.Add((name, states));
      }

    string prefix = $"{def.Domain}:{def.Code}";
    IEnumerable<Registered> codes = [new Registered(prefix, [])];
    foreach (var (name, states) in groups)
      codes = codes.SelectMany(c =>
        states.Select(s => new Registered(
          c.Code + "-" + s,
          [.. c.Variants, (name, s)]
        ))
      );
    return codes;
  }

  /// <summary>Every concrete block <paramref name="domain"/> registers, from its own assembly.</summary>
  public static IEnumerable<Registered> ForDomain(
    string domain,
    Assembly asm
  ) =>
    DefinitionGoldens
      .Collect(domain, asm)
      .OfType<ExBlockDef>()
      .SelectMany(d => Expand(d))
      .DistinctBy(r => r.Code);

  /// <summary>The same expansion as <see cref="ForDomain"/> but with worldproperty groups left as
  /// <c>*</c>, for testing code membership.</summary>
  public static IEnumerable<string> PatternsForDomain(
    string domain,
    Assembly asm
  ) =>
    DefinitionGoldens
      .Collect(domain, asm)
      .OfType<ExBlockDef>()
      .SelectMany(d => Expand(d, propertyGroupsAsWildcard: true))
      .Select(r => r.Code)
      .Distinct();
}
