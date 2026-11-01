#if GAME_GE_1_22
namespace ExpandedLib.WorldGen;

/// <summary>A stateless hash of a body seed and a position, so every chunk that meets a column rolls
/// the same value for it.</summary>
internal static class PositionHash
{
  /// <summary>A value in [0, 1) for the seed, block position and salt.</summary>
  public static float Fraction(int seed, int x, int y, int z, int salt) =>
    (Mix(Mix(Mix(Mix((uint)seed, x), y), z), salt) >> 8) / 16777216f;

  /// <summary>An index in [0, count) for the seed and salt; <paramref name="count"/> is above 0.</summary>
  public static int Index(int seed, int salt, int count) =>
    (int)(Mix((uint)seed, salt) % (uint)count);

  private static uint Mix(uint h, int v)
  {
    h ^= (uint)v + 0x9E3779B9u + (h << 6) + (h >> 2);
    h *= 0x85EBCA6Bu;
    h ^= h >> 13;
    h *= 0xC2B2AE35u;
    h ^= h >> 16;
    return h;
  }
}
#endif
