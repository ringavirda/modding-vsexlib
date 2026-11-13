using System;

namespace ExpandedLib.Industry.Benches;

/// <summary>
/// The cut of a bench stroke on the crank: how much of the input shaft's turn a blade or die spends in the
/// stock, and the torque that costs. A stroke's cut ends at half the stroke, the blade's bottom.
/// </summary>
public static class StrokeCut {
  /// <summary>The input-shaft angle a cut spans, ratio x acos(1 - 2t/s).</summary>
  /// <param name="depthVx">Depth cut in voxels, zero or more; at or over <paramref name="travelVx"/> the whole
  /// travel is cut.</param>
  /// <param name="travelVx">The drawn blade travel at the stock in voxels, above zero.</param>
  /// <param name="ratio">Input-shaft radians per radian of the crank the blade rides (2 on the 2:1 machines).</param>
  /// <returns>Radians, 0 to ratio x pi; zero for a depth of zero or less.</returns>
  public static float Arc(float depthVx, float travelVx, float ratio) {
    if (depthVx <= 0f)
      return 0f;
    if (depthVx >= travelVx)
      return ratio * MathF.PI;
    return ratio * MathF.Acos(1f - 2f * depthVx / travelVx);
  }

  /// <summary>The torque a cut holds over its arc.</summary>
  /// <param name="energyJ">The cut's energy in joules, zero or more.</param>
  /// <param name="arcRad">The cut's arc in radians, from <see cref="Arc"/>.</param>
  /// <returns>N*m; zero when the arc is zero or less.</returns>
  public static float Torque(float energyJ, float arcRad) =>
    arcRad > 0f ? energyJ / arcRad : 0f;

  /// <summary>Whether the stroke's angle lies in the cut, the arc that ends at half the stroke.</summary>
  /// <param name="angleRad">Radians turned since the stroke began.</param>
  /// <param name="strokeRad">The whole stroke in radians, above zero.</param>
  /// <param name="arcRad">The cut's arc in radians.</param>
  public static bool InCut(float angleRad, float strokeRad, float arcRad) =>
    angleRad >= strokeRad / 2f - arcRad && angleRad <= strokeRad / 2f;

  /// <summary>The part of an advance of the stroke's angle that lies in the cut.</summary>
  /// <param name="startRad">Radians turned before the advance.</param>
  /// <param name="advanceRad">Radians the advance turns, above zero.</param>
  /// <param name="strokeRad">The whole stroke in radians, above zero.</param>
  /// <param name="arcRad">The cut's arc in radians.</param>
  /// <returns>0 to 1; zero for an advance of zero or less.</returns>
  public static float CutShare(
    float startRad,
    float advanceRad,
    float strokeRad,
    float arcRad
  ) {
    if (advanceRad <= 0f)
      return 0f;
    float from = MathF.Max(startRad, strokeRad / 2f - arcRad);
    float to = MathF.Min(startRad + advanceRad, strokeRad / 2f);
    return MathF.Max(0f, to - from) / advanceRad;
  }
}
