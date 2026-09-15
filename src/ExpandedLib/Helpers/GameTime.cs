using System;

namespace ExpandedLib.Helpers;

/// <summary>Advances a machine on game time, the world calendar, rather than real time.</summary>
public static class GameTime {
  /// <summary>Game-seconds between two <c>Calendar.TotalHours</c> readings; never negative.</summary>
  public static double SecondsBetween(double fromHours, double toHours) =>
    Math.Max(0.0, toHours - fromHours) * 3600.0;

  /// <summary>Replays <paramref name="elapsedSeconds"/> as sub-steps of at most <paramref name="stepSeconds"/>, calling <paramref name="step"/> once per sub-step, capped at <paramref name="maxSteps"/>.</summary>
  public static int CatchUp(
    double elapsedSeconds,
    float stepSeconds,
    int maxSteps,
    Action<float> step
  ) {
    if (stepSeconds <= 0f || maxSteps <= 0 || elapsedSeconds <= 0.0)
      return 0;

    double remaining = Math.Min(elapsedSeconds, (double)stepSeconds * maxSteps);
    int steps = 0;
    while (remaining > 1e-6 && steps < maxSteps) {
      float dt = (float)Math.Min(remaining, stepSeconds);
      step(dt);
      remaining -= dt;
      steps++;
    }
    return steps;
  }
}
