using System;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Machining;

/// <summary>The transform that places a mounted piece's mesh at a <see cref="MachineAnchor"/> on a block.</summary>
public static class MachineAnchors {
  /// <summary>
  /// The matrix that takes a piece's mesh, in its own unit cell, to the block's cell: scaled and rotated about the
  /// cell's centre, moved so that centre lies at the anchor's origin, then turned with the block about the block's
  /// centre.
  /// </summary>
  /// <param name="anchor">Where the piece sits; origin in voxels, rotation in degrees, scale above 0.</param>
  /// <param name="blockRotationDeg">The block's shape rotation about X, Y and Z, degrees.</param>
  /// <returns>A column-major 4x4 matrix in blocks (16 voxels to the block).</returns>
  /// <exception cref="ArgumentNullException"><paramref name="anchor"/> or <paramref name="blockRotationDeg"/> is null.</exception>
  public static float[] Compose(MachineAnchor anchor, Vec3f blockRotationDeg) {
    ArgumentNullException.ThrowIfNull(anchor);
    ArgumentNullException.ThrowIfNull(blockRotationDeg);
    float[] m = Mat4f.Create();
    Mat4f.Translate(m, m, 0.5f, 0.5f, 0.5f);
    Mat4f.RotateX(m, m, blockRotationDeg.X * GameMath.DEG2RAD);
    Mat4f.RotateY(m, m, blockRotationDeg.Y * GameMath.DEG2RAD);
    Mat4f.RotateZ(m, m, blockRotationDeg.Z * GameMath.DEG2RAD);
    Mat4f.Translate(m, m, -0.5f, -0.5f, -0.5f);
    Mat4f.Translate(
      m,
      m,
      anchor.Origin.X / 16f,
      anchor.Origin.Y / 16f,
      anchor.Origin.Z / 16f
    );
    Mat4f.RotateX(m, m, anchor.Rotation.X * GameMath.DEG2RAD);
    Mat4f.RotateY(m, m, anchor.Rotation.Y * GameMath.DEG2RAD);
    Mat4f.RotateZ(m, m, anchor.Rotation.Z * GameMath.DEG2RAD);
    Mat4f.Scale(m, m, [anchor.Scale, anchor.Scale, anchor.Scale]);
    Mat4f.Translate(m, m, -0.5f, -0.5f, -0.5f);
    return m;
  }
}
