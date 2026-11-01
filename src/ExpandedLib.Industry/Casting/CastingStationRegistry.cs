using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Casting;

/// <summary>
/// The merged catalogue of casting station kinds, keyed by code. A kind redeclared differently is
/// reported and the first stands. World-free; runs headless.
/// </summary>
public sealed class CastingStationRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static CastingStationRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by the station loader after its JSON
  /// read on every load; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly Dictionary<string, CastingStationKind> _byCode = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>
  /// Merges <paramref name="set"/>. An identical redeclaration of a held kind is a no-op; a different
  /// one is ignored. Returns one human-readable message per ignored declaration.
  /// </summary>
  public IReadOnlyList<string> Contribute(CastingStationSet set) {
    var conflicts = new List<string>();
    foreach (CastingStationKind kind in set.Stations) {
      if (!_byCode.TryGetValue(kind.Code, out CastingStationKind? held)) {
        _byCode[kind.Code] = kind;
        continue;
      }
      if (!held.SameAs(kind))
        conflicts.Add(
          $"station '{kind.Code}' is already declared with different moulds or sand, so this "
            + "declaration is ignored; the first declaration stands"
        );
    }
    return conflicts;
  }

  /// <summary>Looks up the kind declared as <paramref name="code"/>, ignoring case.</summary>
  public bool TryGet(
    string? code,
    [NotNullWhen(true)] out CastingStationKind? kind
  ) {
    kind = null;
    return code != null && _byCode.TryGetValue(code, out kind);
  }

  /// <summary>Every declared kind.</summary>
  public IReadOnlyCollection<CastingStationKind> All => _byCode.Values;

  /// <summary>How many kinds are declared.</summary>
  public int Count => _byCode.Count;

  /// <summary>Drops every kind. The loader clears before repopulating on each world load.</summary>
  public void Clear() => _byCode.Clear();
}
