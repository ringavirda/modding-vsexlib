using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// The merged catalogue of machine kinds, keyed by code. A kind redeclared with different figures is
/// reported and the first stands. World-free; runs headless.
/// </summary>
public sealed class MachineKindRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static MachineKindRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by the loader after its JSON
  /// read on every load; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly List<MachineKind> _kinds = [];

  /// <summary>
  /// Merges <paramref name="set"/>. An identical redeclaration is a no-op; a different one is ignored.
  /// Returns one human-readable message per ignored declaration.
  /// </summary>
  public IReadOnlyList<string> Contribute(MachineKindSet set) {
    var conflicts = new List<string>();
    foreach (MachineKind kind in set.Kinds) {
      MachineKind? held = _kinds.FirstOrDefault(k =>
        string.Equals(k.Code, kind.Code, StringComparison.OrdinalIgnoreCase)
      );
      if (held == null)
        _kinds.Add(kind);
      else if (!held.SameAs(kind))
        conflicts.Add(
          $"machine kind '{kind.Code}' is already declared with different figures, so this "
            + "declaration is ignored; the first declaration stands"
        );
    }
    return conflicts;
  }

  /// <summary>Looks up the kind declared as <paramref name="code"/>, ignoring case.</summary>
  public bool TryGet(string? code, [NotNullWhen(true)] out MachineKind? kind) {
    kind =
      code == null
        ? null
        : _kinds.FirstOrDefault(k =>
          string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase)
        );
    return kind != null;
  }

  /// <summary>Every declared kind, in declaration order.</summary>
  public IReadOnlyList<MachineKind> All => _kinds;

  /// <summary>Every kind that does <paramref name="step"/>, in declaration order; empty when none does.</summary>
  public IReadOnlyList<MachineKind> Offering(string? step) =>
    [.. _kinds.Where(k => k.StepOf(step) != null)];

  /// <summary>Drops every kind. The loader clears before repopulating on each world load.</summary>
  public void Clear() => _kinds.Clear();
}
