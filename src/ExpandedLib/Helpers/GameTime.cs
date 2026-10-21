using System;
using ExpandedLib.FastForwarding;
using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>Advances a machine on game time, the world calendar, rather than real time.</summary>
public static class GameTime {
  /// <summary>Machine seconds, the unit of every real-time rate in a machine's tick, that make one game hour at the default calendar speed.</summary>
  public const int MachineSecondsPerHour = 120;

  /// <summary>The calendar time, in game hours since the world's start, a machine reads as now: the cursor of the replay step running on this thread, else <c>Calendar.TotalHours</c>.</summary>
  public static double NowHours(IWorldAccessor world) =>
    FastForward.Replaying ? FastForward.CursorHours : world.Calendar.TotalHours;

  /// <summary>Game-seconds between two <c>Calendar.TotalHours</c> readings; never negative.</summary>
  public static double SecondsBetween(double fromHours, double toHours) =>
    Math.Max(0.0, toHours - fromHours) * 3600.0;

  /// <summary>Replays <paramref name="elapsed"/> as sub-steps of at most <paramref name="step"/>, calling <paramref name="action"/> once per sub-step with its length, capped at <paramref name="maxSteps"/>. <paramref name="elapsed"/>, <paramref name="step"/> and the length handed to <paramref name="action"/> share one unit, whatever the caller counts in (seconds, game hours).</summary>
  /// <returns>The number of sub-steps run; 0 when <paramref name="elapsed"/> is 0 or less, or <paramref name="step"/> or <paramref name="maxSteps"/> is not above 0.</returns>
  public static int CatchUp(
    double elapsed,
    float step,
    int maxSteps,
    Action<float> action
  ) {
    if (step <= 0f || maxSteps <= 0 || elapsed <= 0.0)
      return 0;

    double remaining = Math.Min(elapsed, (double)step * maxSteps);
    int steps = 0;
    while (remaining > 1e-6 && steps < maxSteps) {
      float length = (float)Math.Min(remaining, step);
      action(length);
      remaining -= length;
      steps++;
    }
    return steps;
  }
}
