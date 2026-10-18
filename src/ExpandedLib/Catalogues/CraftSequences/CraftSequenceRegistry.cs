using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;

namespace ExpandedLib.Catalogues;

/// <summary>
/// The merged catalogue of every craft sequence, keyed by machine and then by recipe. A second
/// sequence for one recipe on one machine is reported and the first stands. World-free; runs headless.
/// </summary>
public sealed class CraftSequenceRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static CraftSequenceRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by <see cref="CraftSequenceLoader"/>
  /// after its JSON read on every <c>Load(ICoreAPI)</c>; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly Dictionary<string, List<CraftSequence>> _byMachine = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>
  /// Merges <paramref name="set"/> into the machine it names. Returns one human-readable message per
  /// sequence whose recipe the machine already has a sequence for.
  /// </summary>
  public IReadOnlyList<string> Contribute(CraftSequenceSet set) {
    if (!_byMachine.TryGetValue(set.Machine, out List<CraftSequence>? held))
      _byMachine[set.Machine] = held = [];

    var conflicts = new List<string>();
    foreach (CraftSequence sequence in set.Sequences) {
      if (
        held.Any(s =>
          string.Equals(
            s.Recipe,
            sequence.Recipe,
            StringComparison.OrdinalIgnoreCase
          )
        )
      ) {
        conflicts.Add(
          $"{set.Machine}: '{sequence.Recipe}' already has a sequence, so this one is ignored; the first declaration stands"
        );
        continue;
      }
      held.Add(sequence);
    }
    return conflicts;
  }

  /// <summary>Every sequence <paramref name="machine"/> works, in declaration order.</summary>
  public IReadOnlyList<CraftSequence> Sequences(string? machine) =>
    machine != null
    && _byMachine.TryGetValue(machine, out List<CraftSequence>? sequences)
      ? sequences
      : [];

  /// <summary>The sequence <paramref name="machine"/> has for the grid recipe named
  /// <paramref name="recipe"/>, or null.</summary>
  public CraftSequence? Sequence(string? machine, string? recipe) =>
    recipe == null
      ? null
      : Sequences(machine)
        .FirstOrDefault(s =>
          string.Equals(s.Recipe, recipe, StringComparison.OrdinalIgnoreCase)
        );

  /// <summary>The sequence any machine has for the grid recipe named <paramref name="recipe"/>,
  /// the earliest machine first, or null.</summary>
  public CraftSequence? Sequence(string? recipe) =>
    _byMachine
      .Keys.Select(m => Sequence(m, recipe))
      .FirstOrDefault(s => s != null);

  /// <summary>The machines with at least one sequence.</summary>
  public IReadOnlyCollection<string> Machines => _byMachine.Keys;

  /// <summary>Drops every sequence. The loader clears before repopulating on each world load.</summary>
  public void Clear() => _byMachine.Clear();
}
