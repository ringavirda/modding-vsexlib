#if GAME_GE_1_22
using System;

namespace ExpandedLib.WorldGen;

/// <summary>
/// A rotated superellipse in the horizontal plane: u runs along the dip, v along the strike,
/// and a column is inside when |u/a|^n + |v/b|^n is at most the noisy edge.
/// </summary>
/// <param name="run">Whole length along the dip, in blocks.</param>
/// <param name="strike">Whole length along the strike, in blocks.</param>
/// <param name="exponent">Superellipse exponent n.</param>
/// <param name="taperFull">Normalised distance with taper factor 1.</param>
/// <param name="taperZero">Normalised distance with taper factor 0.</param>
/// <param name="direction">Dip direction in radians, the angle of u from the x axis toward z.</param>
internal sealed class DepositFootprint(
  float run,
  float strike,
  float exponent,
  float taperFull,
  float taperZero,
  float direction
)
{
  /// <summary>Longest run or strike in blocks, the reach of three chunks.</summary>
  public const float MaxLength = 160;

  /// <summary>Mean of the edge noise factor, 1 - 0.2 x noise with noise averaging 0.5.</summary>
  public const double EdgeMean = 0.9;

  /// <summary>The edge a column must stay within, for a noise value 0..1.</summary>
  public static float Edge(float noise) => 1 - 0.2f * noise;

  /// <summary>Distance down the dip from the updip edge, for u.</summary>
  public float DistanceDown(float u) => u + run / 2;

  /// <summary>The offsets from the body centre as (u, v).</summary>
  public (float U, float V) Locate(float dx, float dz)
  {
    float cos = MathF.Cos(direction);
    float sin = MathF.Sin(direction);
    return (dx * cos + dz * sin, -dx * sin + dz * cos);
  }

  /// <summary>|u/a|^n + |v/b|^n, 1 on the nominal outline.</summary>
  public float S(float u, float v) =>
    MathF.Pow(MathF.Abs(u / (run / 2)), exponent)
    + MathF.Pow(MathF.Abs(v / (strike / 2)), exponent);

  /// <summary>Whether the column at the offset from the body centre is inside the outline.</summary>
  public bool Contains(float dx, float dz, float noise)
  {
    var (u, v) = Locate(dx, dz);
    return S(u, v) <= Edge(noise);
  }

  /// <summary>The thickness factor 0..1 of a column at s inside edge e.</summary>
  public float Taper(float s, float edge) =>
    Math.Clamp((taperZero - s / edge) / (taperZero - taperFull), 0f, 1f);

  /// <summary>The base plane height of the column at u, falling <paramref name="dip"/> per block
  /// down the dip from <paramref name="anchorY"/> at the updip edge.</summary>
  public float BaseY(float anchorY, float dip, float u) =>
    anchorY - dip * DistanceDown(u);

  /// <summary>Area of the nominal outline in square blocks.</summary>
  public static double Area(float run, float strike, float exponent) =>
    run
    * strike
    * Gamma(1 + 1 / exponent)
    * Gamma(1 + 1 / exponent)
    / Gamma(1 + 2 / exponent);

  /// <summary>The mean taper factor over the nominal area at edge 1: the area inside s is
  /// proportional to s^(2/n), integrated by a 1000-step midpoint sum.</summary>
  public static double MeanTaper(
    float exponent,
    float taperFull,
    float taperZero
  )
  {
    const int steps = 1000;
    double power = 2.0 / exponent;
    double sum = 0;
    for (int i = 0; i < steps; i++)
    {
      double s = (i + 0.5) / steps;
      double weight =
        Math.Pow((i + 1.0) / steps, power) - Math.Pow((double)i / steps, power);
      sum +=
        Math.Clamp((taperZero - s) / (taperZero - taperFull), 0, 1) * weight;
    }
    return sum;
  }

  /// <summary>The mean taper factor over the nominal area once the edge noise has shrunk the
  /// outline to <see cref="EdgeMean"/>^(2/n) of it.</summary>
  public static double ExpectedFactor(
    float exponent,
    float taperFull,
    float taperZero
  ) =>
    MeanTaper(exponent, taperFull, taperZero)
    * Math.Pow(EdgeMean, 2.0 / exponent);

  private static readonly double[] Lanczos =
  [
    0.99999999999980993,
    676.5203681218851,
    -1259.1392167224028,
    771.32342877765313,
    -176.61502916214059,
    12.507343278686905,
    -0.13857109526572012,
    9.9843695780195716e-6,
    1.5056327351493116e-7,
  ];

  private static double Gamma(double x)
  {
    x -= 1;
    double sum = Lanczos[0];
    double t = x + 7.5;
    for (int i = 1; i < Lanczos.Length; i++)
    {
      sum += Lanczos[i] / (x + i);
    }
    return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * sum;
  }
}
#endif
