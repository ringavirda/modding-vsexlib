#if GAME_GE_1_22
using System;

namespace ExpandedLib.WorldGen;

/// <summary>Smooth value noise of a body seed and a world position, so every chunk that meets a
/// column gets the same value for it.</summary>
internal static class SeamNoise
{
  /// <summary>A value in [-1, 1] that changes slowly over <paramref name="wavelength"/> blocks.</summary>
  /// <param name="seed">The body seed.</param>
  /// <param name="salt">Separates the waves of one body.</param>
  /// <param name="x">World x in blocks.</param>
  /// <param name="z">World z in blocks.</param>
  /// <param name="wavelength">Blocks between lattice points; 0 or less gives 0.</param>
  public static float Wave(
    int seed,
    int salt,
    float x,
    float z,
    float wavelength
  )
  {
    if (wavelength <= 0)
    {
      return 0f;
    }
    float gx = x / wavelength;
    float gz = z / wavelength;
    int ix = (int)MathF.Floor(gx);
    int iz = (int)MathF.Floor(gz);
    float fx = Smooth(gx - ix);
    float fz = Smooth(gz - iz);
    float a = Lattice(seed, salt, ix, iz);
    float b = Lattice(seed, salt, ix + 1, iz);
    float c = Lattice(seed, salt, ix, iz + 1);
    float d = Lattice(seed, salt, ix + 1, iz + 1);
    float top = a + (b - a) * fx;
    float bottom = c + (d - c) * fx;
    return top + (bottom - top) * fz;
  }

  private static float Smooth(float t) => t * t * (3 - 2 * t);

  private static float Lattice(int seed, int salt, int ix, int iz) =>
    PositionHash.Fraction(seed, ix, 0, iz, salt) * 2 - 1;
}
#endif
