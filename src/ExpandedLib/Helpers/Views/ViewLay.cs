using System;
using System.Linq;
using Vintagestory.API.MathTools;
using static ExpandedLib.Helpers.CollectibleViews;

namespace ExpandedLib.Helpers;

/// <summary>A drawn box in its lay frame: three oriented unit axes in the shape's frame, longest first, and
/// the box's interval along each in voxels.</summary>
internal sealed record Lay(Vec3d[] Axes, double[] Lo, double[] Hi) {
  internal double Extent(int k) => Hi[k] - Lo[k];

  internal double[] Coords(Vec3d p) =>
    [.. Axes.Select(a => a.X * p.X + a.Y * p.Y + a.Z * p.Z)];

  internal Vec3d Point(double[] u) =>
    new(
      Axes.Select((a, k) => a.X * u[k]).Sum(),
      Axes.Select((a, k) => a.Y * u[k]).Sum(),
      Axes.Select((a, k) => a.Z * u[k]).Sum()
    );

  /// <summary>The lay of <paramref name="box"/> with its axes ordered longest first, ties by frame axis. With
  /// <paramref name="oriented"/> the longest axis points at the end whose last tenth meets more drawn elements,
  /// the larger sum of their cross-sections on a tie, or along <paramref name="tip"/> (a sign and an axis letter, "+y") when given.</summary>
  /// <exception cref="ArgumentException"><paramref name="tip"/> is not a sign and one of x, y, z.</exception>
  internal static Lay Of(ViewBox box, bool oriented, string? tip) {
    double[] ext =
    [
      box.To.X - box.From.X,
      box.To.Y - box.From.Y,
      box.To.Z - box.From.Z,
    ];
    int[] order =
    [
      .. Enumerable
        .Range(0, 3)
        .OrderBy(i => -Math.Round(ext[i], 4))
        .ThenBy(i => i),
    ];
    Vec3d[] axes = [.. order.Select(i => box.Axes[i])];
    double[] lo = [.. order.Select(i => Component(box.From, i))];
    double[] hi = [.. order.Select(i => Component(box.To, i))];
    var lay = new Lay(axes, lo, hi);
    return oriented && WorkingEndIsLow(lay, box, tip) ? lay.FlipLongest() : lay;
  }

  private Lay FlipLongest() =>
    new(
      [new Vec3d(-Axes[0].X, -Axes[0].Y, -Axes[0].Z), Axes[1], Axes[2]],
      [-Hi[0], Lo[1], Lo[2]],
      [-Lo[0], Hi[1], Hi[2]]
    );

  private static bool WorkingEndIsLow(Lay lay, ViewBox box, string? tip) {
    if (tip != null) {
      if (
        tip.Length != 2
        || (tip[0] != '+' && tip[0] != '-')
        || "xyz".IndexOf(tip[1]) < 0
      )
        throw new ArgumentException(
          $"tip '{tip}' is not a sign and one of x, y, z",
          nameof(tip)
        );
      Vec3d d = lay.Axes[0];
      double along = Component(d, "xyz".IndexOf(tip[1]));
      return tip[0] == '+' ? along < 0 : along > 0;
    }
    double zone = ViewReferences.TipFraction * lay.Extent(0);
    int lowCount = 0,
      highCount = 0;
    double lowCross = 0,
      highCross = 0;
    foreach (Vec3d[] corners in box.Drawn) {
      double[][] u = [.. corners.Select(lay.Coords)];
      double cross =
        (u.Max(c => c[1]) - u.Min(c => c[1]))
        * (u.Max(c => c[2]) - u.Min(c => c[2]));
      if (u.Min(c => c[0]) < lay.Lo[0] + zone) {
        lowCount++;
        lowCross += cross;
      }
      if (u.Max(c => c[0]) > lay.Hi[0] - zone) {
        highCount++;
        highCross += cross;
      }
    }
    return lowCount != highCount
      ? lowCount > highCount
      : lowCross > highCross + 1e-6;
  }

  internal static double Component(Vec3d v, int i) =>
    i == 0 ? v.X
    : i == 1 ? v.Y
    : v.Z;
}
