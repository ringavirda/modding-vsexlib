using System;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Machining;

/// <summary>Why a machine will or will not cut the piece in front of it.</summary>
public enum MachineCutVerdict {
  /// <summary>The cut can run.</summary>
  Ready,

  /// <summary>No job for this piece at a step this machine kind declares.</summary>
  NoJob,

  /// <summary>No tool is fitted.</summary>
  NoTool,

  /// <summary>The fitted tool is not the kind the step takes.</summary>
  WrongTool,

  /// <summary>The fitted tool's temper is below the job's floor.</summary>
  TierShort,

  /// <summary>The job's fixture is not in place.</summary>
  NoFixture,

  /// <summary>The chips slot cannot take the job's byproducts.</summary>
  ChipsFull,

  /// <summary>The drive is not turning.</summary>
  NoDrive,
}

/// <summary>
/// The world-free arithmetic of a machine tool's cut: whether it may start, the load it puts on its drive,
/// the progress a tick credits and the time the same job takes by hand.
/// </summary>
public static class MachineCut {
  /// <summary>Game seconds that pass in one real second, the machine's own conversion for held hand time.</summary>
  public const double GameSecondsPerRealSecond = 30d;

  /// <summary>
  /// Decides whether <paramref name="job"/> can run at <paramref name="kind"/>; the first failing check is returned,
  /// in the order of <see cref="MachineCutVerdict"/>.
  /// </summary>
  /// <param name="job">The job for the piece mounted, or null when there is none.</param>
  /// <param name="kind">The machine kind.</param>
  /// <param name="tool">The fitted tool's spec, or null when none is fitted.</param>
  /// <param name="fixturePresent">Whether the item the job's fixture names is in place.</param>
  /// <param name="chipsFree">Whether the chips slot can take the job's byproducts.</param>
  /// <param name="speed">The drive's speed, rad/s; 0 or less is no drive.</param>
  public static MachineCutVerdict Verdict(
    MachiningJob? job,
    MachineKind kind,
    MachineToolSpec? tool,
    bool fixturePresent,
    bool chipsFree,
    float speed
  ) {
    MachineKindStep? step = job is null ? null : kind.StepOf(job.Step);
    if (job is null || step is null || !OffersTo(job, kind))
      return MachineCutVerdict.NoJob;
    if (tool is null)
      return MachineCutVerdict.NoTool;
    if (
      !string.Equals(
        tool.Kind,
        step.Tooling,
        StringComparison.OrdinalIgnoreCase
      )
    )
      return MachineCutVerdict.WrongTool;
    if (tool.Tier < job.MinTier)
      return MachineCutVerdict.TierShort;
    if (job.Fixture is not null && !fixturePresent)
      return MachineCutVerdict.NoFixture;
    if (!chipsFree)
      return MachineCutVerdict.ChipsFull;
    if (speed <= 0f)
      return MachineCutVerdict.NoDrive;
    return MachineCutVerdict.Ready;
  }

  /// <summary>Whether <paramref name="job"/> is offered at <paramref name="kind"/>: its machine list is empty or names the kind.</summary>
  public static bool OffersTo(MachiningJob job, MachineKind kind) {
    if (job.Machines.Count == 0)
      return true;
    foreach (string code in job.Machines)
      if (string.Equals(code, kind.Code, StringComparison.OrdinalIgnoreCase))
        return true;
    return false;
  }

  /// <summary>
  /// Energy a resolved job takes, J: its own <c>energy</c>, else its removed mass times the material's energy for
  /// <paramref name="motion"/>.
  /// </summary>
  /// <returns>0 when the job declares no energy and has no resolved material.</returns>
  public static float Energy(
    MachiningJob job,
    CutMotion motion,
    CuttingMaterialRegistry? materials = null
  ) {
    if (job.Energy is { } declared)
      return declared;
    materials ??= CuttingMaterialRegistry.Shared;
    if (
      job.ResolvedMaterial is null
      || !materials.TryGet(job.ResolvedMaterial, out CuttingMaterial? m)
    )
      return 0f;
    return job.RemovedKg * m!.EnergyFor(motion);
  }

  /// <summary>Torque a cut puts on the drive, N*m: the step's power over the kind's rated speed while cutting, 0 idle.</summary>
  public static float LoadTorque(
    MachineKind kind,
    MachineKindStep step,
    bool cutting
  ) => cutting ? step.Power / kind.RatedSpeed : 0f;

  /// <summary>
  /// Joules a tick delivers to the cut: the load's torque times the run's actual speed times the game seconds
  /// elapsed, clamped to <paramref name="remaining"/>.
  /// </summary>
  /// <param name="torque">The cut's load torque, N*m.</param>
  /// <param name="speed">The run's speed, rad/s; 0 or less delivers nothing.</param>
  /// <param name="gameSeconds">Game seconds since the last credit.</param>
  /// <param name="remaining">Joules the job still needs.</param>
  public static double Credit(
    float torque,
    float speed,
    double gameSeconds,
    double remaining
  ) {
    if (speed <= 0f || gameSeconds <= 0d || remaining <= 0d)
      return 0d;
    return Math.Min(torque * (double)speed * gameSeconds, remaining);
  }

  /// <summary>Real seconds the same job takes by hand: its energy over a man's power over <see cref="GameSecondsPerRealSecond"/>.</summary>
  /// <param name="energy">The job's energy, J.</param>
  /// <param name="handPower">A man's sustained power at the motion, W; 0 or less returns 0.</param>
  public static double HandSeconds(double energy, float handPower) =>
    handPower <= 0f ? 0d : energy / handPower / GameSecondsPerRealSecond;
}
