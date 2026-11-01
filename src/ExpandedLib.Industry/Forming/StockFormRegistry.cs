using System;
using System.Collections.Generic;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Forming;

/// <summary>
/// The merged catalogue of stock forms and mill feedstock. A form redeclared differently, a former
/// name another form holds, or an offered code with a second target is reported and the first
/// declaration stands; an identical redeclaration is a no-op. World-free; runs headless.
/// </summary>
public sealed class StockFormRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static StockFormRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by the stock form loader after its
  /// JSON read on every load; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly Dictionary<string, StockForm> _all = new(
    StringComparer.OrdinalIgnoreCase
  );

  private readonly Dictionary<string, StockForm> _former = new(
    StringComparer.OrdinalIgnoreCase
  );

  private readonly Dictionary<string, string> _feedstock = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>Every registered form, by name. Former names are not listed.</summary>
  public IReadOnlyDictionary<string, StockForm> All => _all;

  /// <summary>Every admitted feedstock code, and the stock item it is converted into on entry.</summary>
  public IReadOnlyDictionary<string, string> Feedstock => _feedstock;

  /// <summary>Merges <paramref name="set"/>. Returns one human-readable message per declaration that
  /// clashes with a held one.</summary>
  public IReadOnlyList<string> Contribute(StockFormSet set) {
    var conflicts = new List<string>();
    foreach (StockForm form in set.Forms) {
      if (_all.TryGetValue(form.Name, out StockForm? held)) {
        if (!held.SameAs(form))
          conflicts.Add(
            $"stock form '{form.Name}' is already declared differently; the first declaration stands"
          );
        continue;
      }
      _all[form.Name] = form;
      foreach (string former in form.FormerNames) {
        if (_former.TryGetValue(former, out StockForm? owner)) {
          if (owner.Name != form.Name)
            conflicts.Add(
              $"stock form '{form.Name}': former name '{former}' already belongs to '{owner.Name}'"
            );
          continue;
        }
        _former[former] = form;
      }
    }
    foreach (FeedstockEntry entry in set.Feedstock) {
      if (_feedstock.TryGetValue(entry.Offered, out string? held)) {
        if (held != entry.EntersAs)
          conflicts.Add(
            $"feedstock '{entry.Offered}' already enters as '{held}', so '{entry.EntersAs}' is ignored; the first declaration stands"
          );
        continue;
      }
      _feedstock[entry.Offered] = entry.EntersAs;
    }
    return conflicts;
  }

  /// <summary>Looks up a form by name, or by a former name; false when none is registered or
  /// <paramref name="name"/> is null.</summary>
  public bool TryGet(string? name, out StockForm? form) {
    form = null;
    return name != null
      && (
        _all.TryGetValue(name, out form) || _former.TryGetValue(name, out form)
      );
  }

  /// <summary>The stock item <paramref name="offered"/> enters the mill as, or null when it is not
  /// admitted feedstock.</summary>
  public string? EntersAs(string? offered) =>
    offered != null && _feedstock.TryGetValue(offered, out string? code)
      ? code
      : null;

  /// <summary>Drops every form, former name and admission. The loader clears before repopulating on
  /// each world load.</summary>
  public void Clear() {
    _all.Clear();
    _former.Clear();
    _feedstock.Clear();
  }
}
