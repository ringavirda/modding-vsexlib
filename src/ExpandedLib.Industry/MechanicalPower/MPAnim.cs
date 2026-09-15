using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>Phase-lock math for mega-block parts that must turn in step with a mechanical-power
/// axle rather than merely at a proportional speed.</summary>
public static class MPAnim {
  /// <summary>Returns the next cyclic frame, wrapped within <c>[0, totalFrames)</c>. Returns 0
  /// when <paramref name="totalFrames"/> is 1 or less.</summary>
  public static float AdvanceFrame(
    float currentFrame,
    float lastAngleRad,
    float angleRad,
    int totalFrames
  ) {
    if (totalFrames <= 1)
      return 0f;
    // AngleRadDistance gives the signed shortest delta.
    float delta = GameMath.AngleRadDistance(lastAngleRad, angleRad);
    return GameMath.Mod(
      currentFrame + delta / GameMath.TWOPI * totalFrames,
      totalFrames
    );
  }

  /// <summary>Maps a network rotation angle <c>0..2*pi</c> onto a cyclic animation frame
  /// <c>0..totalFrames</c>.</summary>
  public static float FrameFromAngle(float angleRad, int totalFrames) {
    if (totalFrames <= 1)
      return 0f;
    float frame = GameMath.Mod(
      angleRad / GameMath.TWOPI * totalFrames,
      totalFrames
    );
    // A ratio landing on a wrap boundary can round up to exactly totalFrames, one past the clip's
    // last valid index; fold it back to 0.
    return frame < totalFrames ? frame : 0f;
  }

  /// <summary>Pins the running <paramref name="animCode"/> clip's frame to
  /// <paramref name="angleRad"/>. Call once per render frame.</summary>
  /// <param name="reverse">Set when the clip's shaft is keyframed turning the opposite way to the
  /// axle.</param>
  public static void LockFrameToAngle(
    AnimationUtil? animUtil,
    string animCode,
    float angleRad,
    bool reverse = false
  ) {
    if (reverse)
      angleRad = -angleRad;
    if (animUtil?.animator?.GetAnimationState(animCode) is not { } state)
      return;
    if (state.Animation == null)
      return;
    state.CurrentFrame = FrameFromAngle(
      angleRad,
      state.Animation.QuantityFrames
    );
  }
}
