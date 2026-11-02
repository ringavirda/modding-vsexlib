using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// The merged catalogue of machining jobs, keyed by step and input. A second job on one (step, input)
/// with a different output or count is reported and the first stands. World-free; runs headless.
/// </summary>
public sealed class MachiningJobRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static MachiningJobRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by the loader after its JSON
  /// read on every load; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly List<MachiningJob> _jobs = [];

  /// <summary>
  /// Merges <paramref name="set"/>. Returns one human-readable message per job whose (step, input)
  /// another job already claims with a different output or count; an identical redeclaration is a no-op.
  /// </summary>
  public IReadOnlyList<string> Contribute(MachiningJobSet set) {
    var conflicts = new List<string>();
    foreach (MachiningJob job in set.Jobs) {
      MachiningJob? held = _jobs.FirstOrDefault(j =>
        j.Matches(job.Step, job.Input)
      );
      if (held != null) {
        if (
          !string.Equals(held.Output, job.Output, StringComparison.Ordinal)
          || held.Count != job.Count
        )
          conflicts.Add(
            $"{job.Step}: '{job.Input}' already yields {held.Count} x '{held.Output}', so "
              + $"{job.Count} x '{job.Output}' is ignored; the first declaration stands"
          );
        continue;
      }
      _jobs.Add(job);
    }
    return conflicts;
  }

  /// <summary>Every declared job, in declaration order.</summary>
  public IReadOnlyList<MachiningJob> All => _jobs;

  /// <summary>Every job at <paramref name="step"/>, in declaration order; empty when the step is unknown.</summary>
  public IReadOnlyList<MachiningJob> Jobs(string? step) =>
    step is null
      ? []
      :
      [
        .. _jobs.Where(j =>
          string.Equals(j.Step, step, StringComparison.OrdinalIgnoreCase)
        ),
      ];

  /// <summary>The job for <paramref name="input"/> at <paramref name="step"/>, or null.</summary>
  public MachiningJob? Job(string? step, string? input) =>
    step is null || input is null
      ? null
      : _jobs.FirstOrDefault(j => j.Matches(step, input));

  /// <summary>The steps that have a job for <paramref name="input"/>, in declaration order.</summary>
  public IReadOnlyList<string> StepsFor(string? input) =>
    input is null
      ? []
      :
      [
        .. _jobs
          .Where(j =>
            string.Equals(j.Input, input, StringComparison.OrdinalIgnoreCase)
          )
          .Select(j => j.Step)
          .Distinct(StringComparer.OrdinalIgnoreCase),
      ];

  /// <summary>Every step that has at least one job, in declaration order.</summary>
  public IReadOnlyList<string> Steps =>
    [.. _jobs.Select(j => j.Step).Distinct(StringComparer.OrdinalIgnoreCase)];

  /// <summary>Drops every job. The loader clears before repopulating on each world load.</summary>
  public void Clear() => _jobs.Clear();
}
