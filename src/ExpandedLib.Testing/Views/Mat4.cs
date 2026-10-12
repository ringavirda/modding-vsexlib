using System;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A 4 x 4 transform in doubles, row-major, applied to column vectors; each call returns a new
/// matrix that is this one followed by the step, so a chain reads in the order the game's renderer calls
/// it and a point meets the last call first.</summary>
internal readonly struct Mat4 {
  private readonly double[] _m;

  private Mat4(double[] m) => _m = m;

  internal static Mat4 Identity =>
    new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

  /// <summary>Reads a game matrix, which stores its columns one after another.</summary>
  internal static Mat4 FromColumnMajor(float[] m) {
    var r = new double[16];
    for (int row = 0; row < 4; row++)
      for (int col = 0; col < 4; col++)
        r[row * 4 + col] = m[col * 4 + row];
    return new Mat4(r);
  }

  internal double this[int row, int col] => _m[row * 4 + col];

  internal Mat4 Mul(Mat4 o) {
    var r = new double[16];
    for (int i = 0; i < 4; i++)
      for (int j = 0; j < 4; j++)
        for (int k = 0; k < 4; k++)
          r[i * 4 + j] += _m[i * 4 + k] * o._m[k * 4 + j];
    return new Mat4(r);
  }

  internal Mat4 Translate(double x, double y, double z) =>
    Mul(new Mat4([1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1]));

  internal Mat4 Scale(double x, double y, double z) =>
    Mul(new Mat4([x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1]));

  internal Mat4 RotateX(double degrees) {
    (double s, double c) = Math.SinCos(degrees * Math.PI / 180);
    return Mul(new Mat4([1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0, 0, 0, 0, 1]));
  }

  internal Mat4 RotateY(double degrees) {
    (double s, double c) = Math.SinCos(degrees * Math.PI / 180);
    return Mul(new Mat4([c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0, 0, 0, 0, 1]));
  }

  internal Mat4 RotateZ(double degrees) {
    (double s, double c) = Math.SinCos(degrees * Math.PI / 180);
    return Mul(new Mat4([c, -s, 0, 0, s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]));
  }

  /// <summary>The game's <c>Rotate(x, y, z)</c>: x, then y, then z in call order, so a point turns about z first.</summary>
  internal Mat4 RotateXYZ(double x, double y, double z) =>
    RotateX(x).RotateY(y).RotateZ(z);

  internal Vec3d Apply(Vec3d p) =>
    new(
      _m[0] * p.X + _m[1] * p.Y + _m[2] * p.Z + _m[3],
      _m[4] * p.X + _m[5] * p.Y + _m[6] * p.Z + _m[7],
      _m[8] * p.X + _m[9] * p.Y + _m[10] * p.Z + _m[11]
    );

  internal Vec3d Position => new(_m[3], _m[7], _m[11]);
}
