using System;
using ExpandedLib.Machines;

namespace ExpandedLib.Testing;

/// <summary>
/// Drives one machine on a <see cref="TestWorld"/> until a condition holds, not for a fixed offset.
/// Each step fires block-entity ticks then the network tick, the order <see cref="Scene.Step"/> uses.
/// </summary>
public abstract class MachineRig(TestWorld world) {
  /// <summary>The world this rig steps.</summary>
  public TestWorld World { get; } = world;

  private void Step(float stepSeconds) {
    World.FireBlockEntityTicks(stepSeconds);
    // The network tick advances a whole server second; a sub-second step still ticks it once.
    World.Tick(Math.Max(1, (int)stepSeconds));
  }

  /// <summary>
  /// Advances the world in <paramref name="stepSeconds"/> steps until <paramref name="until"/> holds.
  /// </summary>
  /// <returns>The seconds elapsed.</returns>
  /// <exception cref="TimeoutException"><paramref name="until"/> never held within
  /// <paramref name="ceilingSeconds"/>.</exception>
  public float RunUntil(
    Func<bool> until,
    float ceilingSeconds,
    float stepSeconds = 1f
  ) {
    for (float elapsed = 0f; elapsed < ceilingSeconds; elapsed += stepSeconds) {
      Step(stepSeconds);
      if (until())
        return elapsed + stepSeconds;
    }

    throw new TimeoutException(
      $"condition did not hold within {ceilingSeconds} s"
    );
  }

  /// <summary>Advances the world for <paramref name="seconds"/>, calling <paramref name="observer"/>
  /// (with the seconds just elapsed) after every step.</summary>
  public void RunLive(
    float seconds,
    Action<float>? observer = null,
    float stepSeconds = 1f
  ) {
    for (float elapsed = 0f; elapsed < seconds; elapsed += stepSeconds) {
      Step(stepSeconds);
      observer?.Invoke(stepSeconds);
    }
  }

  /// <summary>
  /// Runs <paramref name="beforeEachStep"/> before every step, for <paramref name="seconds"/>.
  /// </summary>
  public void RunWhile(
    Action beforeEachStep,
    float seconds,
    float stepSeconds = 1f
  ) {
    for (float elapsed = 0f; elapsed < seconds; elapsed += stepSeconds) {
      beforeEachStep();
      Step(stepSeconds);
    }
  }
}

/// <summary>
/// Test-only hooks for the machine base types, for driving a production tick or access check by hand.
/// </summary>
public static class MachineTestHooks {
  /// <summary>Turns off <see cref="BlockEntityMachineStation"/>'s engine interaction-range check. The
  /// claim check is unaffected.</summary>
  public static void DisablePickRangeCheck(
    this BlockEntityMachineStation station
  ) => station.ValidatePickRange = false;

  /// <summary>Runs one production tick on <paramref name="behavior"/> exactly as the registered
  /// listener would, without registering it.</summary>
  public static void DriveProductionTick(
    this BEBehaviorProductionMachine behavior,
    float dt
  ) => behavior.DriveProductionTick(dt);

  /// <summary>Runs one production tick on <paramref name="machine"/>'s hosted process, the block-entity
  /// counterpart of <see cref="DriveProductionTick(BEBehaviorProductionMachine, float)"/>.</summary>
  public static void DriveProductionTick(
    this BlockEntityProductionMachine machine,
    float dt
  ) => machine.DriveProductionTick(dt);
}
