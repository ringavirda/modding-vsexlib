using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Benches;

/// <summary>Why a blank offered to a bench was or was not worked.</summary>
public enum BenchVerdict {
  /// <summary>The press takes it: the blank is consumed and every bundle it is worth leaves at once.</summary>
  Ok,

  /// <summary>No die is fitted; a bench without one has no work.</summary>
  NoDie,

  /// <summary>The fitted die has no job for this blank. Either the wrong die, or the wrong blank.</summary>
  NoJob,

  /// <summary>The shaft is at rest; a stroke needs a turning run.</summary>
  NotTurning,

  /// <summary>The run cannot deliver the stroke, distinct from <see cref="NotTurning"/>.</summary>
  NotEnoughDrive,
}

/// <summary>The outcome of offering a blank to a bench.</summary>
/// <param name="Verdict">Whether the press takes it, and why not if it does not.</param>
/// <param name="Job">The job the fitted die matched, or null when none did.</param>
public readonly record struct BenchDecision(
  BenchVerdict Verdict,
  ProcessJob? Job
) {
  public bool Accepted => Verdict == BenchVerdict.Ok;
}

/// <summary>
/// Resolves what happens when a blank is offered to a bench. Pure functions, callable
/// without a world.
/// </summary>
public static class BenchFeed {
  /// <summary>Whether the bench works <paramref name="job"/>; refusals are ordered as the player can
  /// fix them, machine then blank then drive.</summary>
  public static BenchDecision Decide(
    bool hasDie,
    ProcessJob? job,
    float availableTorque,
    float speed
  ) {
    if (!hasDie)
      return new BenchDecision(BenchVerdict.NoDie, null);
    if (job == null)
      return new BenchDecision(BenchVerdict.NoJob, null);
    if (speed <= 0f)
      return new BenchDecision(BenchVerdict.NotTurning, job);
    return availableTorque >= job.MinTorque
      ? new BenchDecision(BenchVerdict.Ok, job)
      : new BenchDecision(BenchVerdict.NotEnoughDrive, job);
  }
}
