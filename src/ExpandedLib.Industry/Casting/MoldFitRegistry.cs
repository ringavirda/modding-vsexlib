using System;
using System.Collections.Generic;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Casting;

/// <summary>
/// The merged list of oversize tool-mould types, matched against a mould block's <c>tooltype</c> variant.
/// Lists from every file and contributor union; no declaration can clash. World-free; runs headless.
/// </summary>
public sealed class MoldFitRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static MoldFitRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by the mould fit loader after its JSON
  /// read on every load; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly HashSet<string> _oversize = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>Adds the types of <paramref name="set"/> to the oversize list. Returns one message per ignored
  /// declaration, always empty.</summary>
  public IReadOnlyList<string> Contribute(MoldFitSet set) {
    foreach (string tooltype in set.Oversize)
      _oversize.Add(tooltype);
    return [];
  }

  /// <summary>Whether <paramref name="tooltype"/> is listed oversize, ignoring case; false for null.</summary>
  public bool IsOversizeType(string? tooltype) =>
    tooltype != null && _oversize.Contains(tooltype);

  /// <summary>How many types are listed.</summary>
  public int Count => _oversize.Count;

  /// <summary>Drops every type. The loader clears before repopulating on each world load.</summary>
  public void Clear() => _oversize.Clear();
}
