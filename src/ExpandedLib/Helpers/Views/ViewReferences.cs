using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>The reference numbers the view rule copies from vanilla items: hand scales, rotations, and the
/// grip each reference holds as fractions of its drawn box.</summary>
internal static class ViewReferences {
  /// <summary>What a kind's hand view copies. <paramref name="Axes"/> are the reference box's longest,
  /// middle and shortest axes in its shape's frame, <paramref name="Thickness"/> its shortest extent in
  /// voxels, <paramref name="Rotation"/> its hand rotation in degrees, <paramref name="Grip"/> the held
  /// point as fractions of the box along those axes (outside 0..1 for a point off the box), and
  /// <paramref name="OffDrawn"/> the distance in voxels from that point to the nearest drawn element.</summary>
  internal sealed record HandReference(
    Vec3d[] Axes,
    double Thickness,
    Vec3d Rotation,
    double Scale,
    double[] Grip,
    double OffDrawn
  );

  private static readonly Vec3d X = new(1, 0, 0);
  private static readonly Vec3d Y = new(0, 1, 0);
  private static readonly Vec3d Z = new(0, 0, 1);

  internal static readonly HandReference Spear = new(
    [X, Y, Z],
    2.34,
    new Vec3d(-90, 0, 0),
    0.80,
    [0.38, 0.53, 0.71],
    0
  );

  internal static readonly HandReference MetalPlate = new(
    [X, Z, Y],
    1,
    new Vec3d(94, 0, 0),
    0.75,
    [0.44, 0.07, -0.32],
    0.32
  );

  internal static readonly HandReference Ingot = new(
    [Z, X, Y],
    2,
    new Vec3d(0, -71, 18),
    0.75,
    [0.74, 0.48, 0.95],
    0
  );

  internal static readonly HandReference Rod = new(
    [X, Y, Z],
    2,
    new Vec3d(85, 0, 2),
    0.68,
    [0.24, 0.44, 0.87],
    0
  );

  /// <summary>The right hand of the seraph at frame 0 of holdbothhandslarge, in voxels of the seraph shape.</summary>
  internal static readonly Vec3d HoldBothHandsLargeRightHand = new(
    -0.01,
    16.17,
    5.10
  );

  /// <summary>The rotation about Y in degrees that the seraph's right-hand attachment point carries and a hand rotation adds to.</summary>
  internal const double HandYaw = 180;

  /// <summary>A fraction of the last stretch of a longtool's length that decides which end works.</summary>
  internal const double TipFraction = 0.1;

  internal const string SpearIdle = "spearidle";
  internal const string SpearHit = "spearhit";
  internal const string BulkHold = "holdbothhandslarge";
}
