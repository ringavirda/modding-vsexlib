using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// The merged catalogue of machining jobs, keyed by step, input and fixture. A second job on one (step, input)
/// that names the same fixture, or when either names none, with a different output or count is reported and the
/// first stands. World-free; runs headless.
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
  /// Merges <paramref name="set"/>. Returns one human-readable message per job whose (step, input) another
  /// job already claims, with the same fixture or with either fixture-less, and a different output or count;
  /// an identical redeclaration is a no-op. Jobs on one (step, input) with different fixtures all stand.
  /// </summary>
  public IReadOnlyList<string> Contribute(MachiningJobSet set) {
    var conflicts = new List<string>();
    foreach (MachiningJob job in set.Jobs) {
      MachiningJob? held = _jobs.FirstOrDefault(j =>
        j.Matches(job.Step, job.Input)
        && (
          j.Fixture is null
          || job.Fixture is null
          || string.Equals(
            j.Fixture,
            job.Fixture,
            StringComparison.OrdinalIgnoreCase
          )
        )
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

  /// <summary>The job for <paramref name="input"/> at <paramref name="step"/> under the laid
  /// <paramref name="fixture"/>: the job whose fixture is that item, else the input's fixture-less job.</summary>
  /// <param name="step">The step.</param>
  /// <param name="input">Item code of the piece.</param>
  /// <param name="fixture">Item code of the laid fixture, or null when none is laid.</param>
  /// <returns>The job, or null when none fits, including when every job on the input needs a fixture that is not laid.</returns>
  public MachiningJob? Job(string? step, string? input, string? fixture = null) {
    if (step is null || input is null)
      return null;
    MachiningJob? bare = null;
    foreach (MachiningJob job in JobsFor(step, input)) {
      if (job.Fixture is null)
        bare ??= job;
      else if (
        fixture is not null
        && string.Equals(
          job.Fixture,
          fixture,
          StringComparison.OrdinalIgnoreCase
        )
      )
        return job;
    }
    return bare;
  }

  /// <summary>Every job for <paramref name="input"/> at <paramref name="step"/>, whatever its fixture, in declaration order.</summary>
  /// <param name="step">The step.</param>
  /// <param name="input">Item code of the piece.</param>
  public IReadOnlyList<MachiningJob> JobsFor(string? step, string? input) =>
    step is null || input is null
      ? []
      : [.. _jobs.Where(j => j.Matches(step, input))];

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
