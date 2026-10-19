using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Util;

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

  private readonly List<CraftToolDefault> _tools = [CraftToolDefault.Hammer];

  /// <summary>The per-tool stroke defaults, the built-in hammer first and then each declaration in
  /// order.</summary>
  public IReadOnlyList<CraftToolDefault> Tools => _tools;

  /// <summary>Sets what a tool does at each stroke of a step that names nothing of its own. A default
  /// for the same <see cref="CraftToolDefault.Tool"/> pattern replaces the earlier one.</summary>
  /// <param name="tool">The default to add.</param>
  public void DefineTool(CraftToolDefault tool) {
    _tools.RemoveAll(t =>
      string.Equals(t.Tool, tool.Tool, StringComparison.OrdinalIgnoreCase)
    );
    _tools.Add(tool);
  }

  /// <summary>The default of the tool whose item code is <paramref name="toolCode"/>; the latest
  /// declared pattern that matches wins.</summary>
  /// <param name="toolCode">A full item code such as <c>game:hammer-steel</c>; null for bare hands.</param>
  /// <returns>The default, or null when no pattern matches.</returns>
  public CraftToolDefault? ToolDefault(string? toolCode) =>
    toolCode == null
      ? null
      : _tools.LastOrDefault(t => WildcardUtil.Match(t.Tool, toolCode));

  /// <summary>
  /// Merges <paramref name="set"/> into the machine it names. Returns one human-readable message per
  /// sequence whose recipe the machine already has a sequence for.
  /// </summary>
  public IReadOnlyList<string> Contribute(CraftSequenceSet set) {
    if (!_byMachine.TryGetValue(set.Machine, out List<CraftSequence>? held))
      _byMachine[set.Machine] = held = [];

    foreach (CraftToolDefault tool in set.Tools ?? [])
      DefineTool(tool);

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

  /// <summary>Drops every sequence and tool default but the built-in hammer's. The loader clears before
  /// repopulating on each world load.</summary>
  public void Clear() {
    _byMachine.Clear();
    _tools.Clear();
    _tools.Add(CraftToolDefault.Hammer);
  }
}
