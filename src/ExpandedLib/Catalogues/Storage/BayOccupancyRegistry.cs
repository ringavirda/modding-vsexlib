using System;
using System.Collections.Generic;
using ExpandedLib.Registries;

namespace ExpandedLib.Catalogues;

/// <summary>
/// The merged catalogue of what every item occupies, keyed by store; contributed to, not owned. A
/// second rule for an already-sized item is reported and the first stands.
/// </summary>
public sealed class BayOccupancyRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static BayOccupancyRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by <c>BayOccupancyLoader</c> after
  /// its JSON read on every <c>Load(ICoreAPI)</c>.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  private readonly Dictionary<string, List<BayOccupancy>> _byStore = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>Merges <paramref name="set"/> into the store it names; returns one message per rule
  /// whose item is already sized differently.</summary>
  public IReadOnlyList<string> Contribute(BayOccupancySet set) {
    if (!_byStore.TryGetValue(set.Store, out List<BayOccupancy>? rules))
      _byStore[set.Store] = rules = [];

    var conflicts = new List<string>();
    foreach (BayOccupancy rule in set.Rules) {
      BayOccupancy? held = rules.Find(r =>
        string.Equals(r.Item, rule.Item, StringComparison.OrdinalIgnoreCase)
      );
      if (held != null) {
        if (held.Cells != rule.Cells)
          conflicts.Add(
            $"{set.Store}: '{rule.Item}' already occupies {held.Cells} cell(s), so {rule.Cells} is "
              + "ignored; the first declaration stands"
          );
        continue;
      }
      rules.Add(rule);
    }
    return conflicts;
  }

  /// <summary>Every rule <paramref name="store"/> carries, in declaration order.</summary>
  public IReadOnlyList<BayOccupancy> Rules(string? store) =>
    store != null && _byStore.TryGetValue(store, out List<BayOccupancy>? rules)
      ? rules
      : [];

  /// <summary>How many cells <paramref name="code"/> occupies in <paramref name="store"/>, or null if
  /// unlisted; the most specific rule wins.</summary>
  public int? CellsFor(string? store, string? code) {
    if (code == null)
      return null;

    BayOccupancy? best = null;
    foreach (BayOccupancy rule in Rules(store))
      if (
        rule.Matches(code) && (best == null || rule.Precision > best.Precision)
      )
        best = rule;
    return best?.Cells;
  }

  /// <summary>Empties the catalogue; asset reload repopulates it.</summary>
  public void Clear() => _byStore.Clear();
}
