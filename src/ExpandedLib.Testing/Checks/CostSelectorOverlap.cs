using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks that no two recipe-cost selectors in a catalogue can match the same block, compared
/// within a <see cref="RecipeCostEntry.Type"/>.
/// </summary>
public static class CostSelectorOverlap {
  /// <summary>Every pair of same-type catalogue entries whose selectors can both match one code.</summary>
  /// <param name="sampleCodes">The mod's registered codes.</param>
  /// <returns>Empty when none.</returns>
  public static IReadOnlyList<string> Overlaps(
    IReadOnlyDictionary<string, RecipeCostEntry> catalogue,
    IEnumerable<string> sampleCodes
  ) {
    var selectors = catalogue
      .Where(kv => !string.IsNullOrEmpty(kv.Value.Match))
      .Select(kv =>
        (
          Key: kv.Key,
          Type: kv.Value.Type ?? "grid",
          Pattern: new AssetLocation(kv.Value.Match!)
        )
      )
      .ToList();

    var codes = sampleCodes.Select(c => new AssetLocation(c)).ToList();
    var findings = new List<string>();

    for (int i = 0; i < selectors.Count; i++)
      for (int j = i + 1; j < selectors.Count; j++) {
        if (
          !string.Equals(
            selectors[i].Type,
            selectors[j].Type,
            StringComparison.OrdinalIgnoreCase
          )
        )
          continue;

        var both = codes
          .Where(c =>
            WildcardUtil.Match(selectors[i].Pattern, c)
            && WildcardUtil.Match(selectors[j].Pattern, c)
          )
          .Select(c => c.ToString())
          .Take(3)
          .ToList();

        if (both.Count > 0)
          findings.Add(
            $"'{selectors[i].Key}' ({selectors[i].Type} {selectors[i].Pattern}) and "
              + $"'{selectors[j].Key}' ({selectors[j].Type} {selectors[j].Pattern}) "
              + $"both match: {string.Join(", ", both)}"
          );
      }

    return findings;
  }

  /// <summary>The mod's registered codes, read from its own definitions.</summary>
  public static IEnumerable<string> CodesOf(string domain, Assembly asm) =>
    DefinitionCodes.ForDomain(domain, asm).Select(r => r.Code);
}
