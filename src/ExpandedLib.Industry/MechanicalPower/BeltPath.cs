using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A pulley as a belt sees it: the cell it turns in, the axis it turns about and its size.</summary>
/// <param name="Cell">The cell holding the pulley's axis; the axis runs through its centre.</param>
/// <param name="Axis">The axis the pulley turns about.</param>
/// <param name="Size">The pulley size, 1 or 2, as <see cref="MpBeltWear.PulleyDiameter"/> reads it.</param>
public readonly record struct BeltPulley(
  BlockPos Cell,
  EnumAxis Axis,
  int Size
);

/// <summary>One straight run of a belt: a line between the two rims, in the plane the pulleys turn in.</summary>
/// <param name="Axis">The axis the pulleys turn about; the run lies in the plane across it.</param>
/// <param name="Start">The tangent point on the first pulley's rim, in world coordinates.</param>
/// <param name="End">The tangent point on the second pulley's rim, in world coordinates.</param>
public readonly record struct BeltRun(EnumAxis Axis, Vec3d Start, Vec3d End);

/// <summary>
/// The path of an open belt between two pulleys: its two runs, the cells it claims (those its runs cross and those
/// inside its loop, bar the pulleys' own) and the refusal codes. A run on a cell edge crosses neither cell it borders.
/// </summary>
public static class BeltPath {
  /// <summary>The axes of the two pulleys differ.</summary>
  public const string NotParallel = "exlib-belt-notparallel";

  /// <summary>The pulley cells stand apart along the axis, so the pulleys turn in different planes.</summary>
  public const string Plane = "exlib-belt-plane";

  /// <summary>The axes stand further apart than <c>MpBeltReachBlocks</c>.</summary>
  public const string Reach = "exlib-belt-reach";

  /// <summary>The pulleys overlap or touch: their centres are at or under the sum of their radii apart.</summary>
  public const string Overlap = "exlib-belt-overlap";

  /// <summary>A block in a cell the belt claims holds collision boxes that meet the strap or its loop.</summary>
  public const string Blocked = "exlib-belt-blocked";

  /// <summary>Another belt claims a cell this belt claims.</summary>
  public const string CrossBelt = "exlib-belt-crossbelt";

  /// <summary>Width (blocks) of the strap along the axis.</summary>
  public const double WidthBlocks = 4.0 / 16.0;

  private const double Eps = 1e-9;

  /// <summary>Judges whether a belt can be strung between two pulleys, in the order of the codes above.</summary>
  /// <param name="accessor">Reads the blocks in the cells the belt claims.</param>
  /// <param name="a">The first pulley.</param>
  /// <param name="b">The second pulley.</param>
  /// <param name="claimed">Cells the pulleys hold besides their own, which the belt may claim.</param>
  /// <param name="claimedElsewhere">Whether a cell is claimed by another belt; null when no belt claims any.</param>
  /// <returns>The first refusal code that applies, or null when the belt can be strung.</returns>
  public static string? Check(
    IBlockAccessor accessor,
    BeltPulley a,
    BeltPulley b,
    IReadOnlyCollection<BlockPos> claimed,
    System.Func<BlockPos, bool>? claimedElsewhere = null
  ) {
    if (a.Axis != b.Axis)
      return NotParallel;
    if (AxisCoord(a.Cell, a.Axis) != AxisCoord(b.Cell, b.Axis))
      return Plane;
    double distance = Distance(a, b);
    if (distance > ExlibValues.MpBeltReachBlocks)
      return Reach;
    if (distance <= Radius(a) + Radius(b))
      return Overlap;

    var excepted = new HashSet<BlockPos>(claimed) { a.Cell, b.Cell };
    var cells = Footprint(a, b);
    foreach (BlockPos cell in cells)
      if (!excepted.Contains(cell) && Meets(accessor, cell, a, b))
        return Blocked;
    if (claimedElsewhere != null)
      foreach (BlockPos cell in cells)
        if (!excepted.Contains(cell) && claimedElsewhere(cell))
          return CrossBelt;
    return null;
  }

  /// <summary>The two straight runs of the open belt, tangent to both rims on the same side of each.</summary>
  /// <param name="a">The first pulley.</param>
  /// <param name="b">The second pulley.</param>
  /// <returns>Two runs, each from a tangent point on <paramref name="a"/> to one on <paramref name="b"/>.</returns>
  /// <exception cref="ArgumentException">The axes differ, the cells are apart along the axis, or the centres are at or
  /// under the sum of the radii apart.</exception>
  public static BeltRun[] Runs(BeltPulley a, BeltPulley b) {
    if (
      a.Axis != b.Axis
      || AxisCoord(a.Cell, a.Axis) != AxisCoord(b.Cell, b.Axis)
    )
      throw new ArgumentException("The pulleys do not turn in one plane.");
    double d = Distance(a, b);
    double ra = Radius(a);
    double rb = Radius(b);
    if (d <= ra + rb)
      throw new ArgumentException("The pulleys overlap.");

    (int iu, int iv) = PlaneAxes(a.Axis);
    double au = Comp(a.Cell, iu) + 0.5;
    double av = Comp(a.Cell, iv) + 0.5;
    double bu = Comp(b.Cell, iu) + 0.5;
    double bv = Comp(b.Cell, iv) + 0.5;
    double eu = (bu - au) / d;
    double ev = (bv - av) / d;
    double s = (ra - rb) / d;
    double h = Math.Sqrt(1 - s * s);
    var runs = new BeltRun[2];
    for (int i = 0; i < 2; i++) {
      double side = i == 0 ? 1 : -1;
      double nu = s * eu - side * h * ev;
      double nv = s * ev + side * h * eu;
      runs[i] = new BeltRun(
        a.Axis,
        World(a, au + ra * nu, av + ra * nv),
        World(a, bu + rb * nu, bv + rb * nv)
      );
    }
    return runs;
  }

  /// <summary>The cells the two runs cross, each once, in the pulleys' plane. A run lying along the edge between
  /// two cells crosses neither.</summary>
  /// <param name="a">The first pulley.</param>
  /// <param name="b">The second pulley.</param>
  /// <exception cref="ArgumentException">As <see cref="Runs"/>.</exception>
  public static IReadOnlyList<BlockPos> CrossedCells(BeltPulley a, BeltPulley b) {
    var cells = new List<BlockPos>();
    var seen = new HashSet<BlockPos>();
    (int iu, int iv) = PlaneAxes(a.Axis);
    foreach (BeltRun run in Runs(a, b)) {
      double su = Comp(run.Start, iu);
      double sv = Comp(run.Start, iv);
      double eu = Comp(run.End, iu);
      double ev = Comp(run.End, iv);
      for (
        int u = (int)Math.Floor(Math.Min(su, eu));
        u <= Math.Max(su, eu);
        u++
      )
        for (
          int v = (int)Math.Floor(Math.Min(sv, ev));
          v <= Math.Max(sv, ev);
          v++
        )
          if (Crosses(su, sv, eu, ev, u, v, u + 1, v + 1)) {
            BlockPos cell = At(a, u, v);
            if (seen.Add(cell))
              cells.Add(cell);
          }
    }
    return cells;
  }

  /// <summary>The cells inside the belt's loop: those whose interior lies inside the closed outline of the two rim
  /// circles and the two runs, in the pulleys' plane, the pulleys' own cells excepted.</summary>
  /// <param name="a">The first pulley.</param>
  /// <param name="b">The second pulley.</param>
  /// <exception cref="ArgumentException">As <see cref="Runs"/>.</exception>
  public static IReadOnlyList<BlockPos> LoopCells(BeltPulley a, BeltPulley b) {
    Runs(a, b);
    (int iu, int iv) = PlaneAxes(a.Axis);
    double ra = Radius(a);
    double rb = Radius(b);
    double au = Comp(a.Cell, iu) + 0.5;
    double av = Comp(a.Cell, iv) + 0.5;
    double bu = Comp(b.Cell, iu) + 0.5;
    double bv = Comp(b.Cell, iv) + 0.5;
    var cells = new List<BlockPos>();
    for (
      int u = (int)Math.Floor(Math.Min(au - ra, bu - rb));
      u <= Math.Max(au + ra, bu + rb);
      u++
    )
      for (
        int v = (int)Math.Floor(Math.Min(av - ra, bv - rb));
        v <= Math.Max(av + ra, bv + rb);
        v++
      ) {
        BlockPos cell = At(a, u, v);
        if (
          !cell.Equals(a.Cell)
          && !cell.Equals(b.Cell)
          && InLoop(au, av, ra, bu, bv, rb, u, v, u + 1, v + 1)
        )
          cells.Add(cell);
      }
    return cells;
  }

  /// <summary>Every cell the belt claims: the cells its runs cross, then the cells of its loop not among them.</summary>
  /// <param name="a">The first pulley.</param>
  /// <param name="b">The second pulley.</param>
  /// <exception cref="ArgumentException">As <see cref="Runs"/>.</exception>
  public static IReadOnlyList<BlockPos> Footprint(BeltPulley a, BeltPulley b) {
    var cells = new List<BlockPos>(CrossedCells(a, b));
    var seen = new HashSet<BlockPos>(cells);
    foreach (BlockPos cell in LoopCells(a, b))
      if (seen.Add(cell))
        cells.Add(cell);
    return cells;
  }

  /// <summary>Whether the block at a cell holds collision boxes that meet the strap or the inside of the loop: a
  /// box within the strap's width along the axis whose footprint in the plane has area inside the loop. Boxes that
  /// only touch the loop's outline do not. Only the cell's own block is read.</summary>
  /// <param name="accessor">Reads the block.</param>
  /// <param name="pos">The cell.</param>
  /// <param name="a">The first pulley of the belt.</param>
  /// <param name="b">The second pulley of the belt.</param>
  public static bool Meets(
    IBlockAccessor accessor,
    BlockPos pos,
    BeltPulley a,
    BeltPulley b
  ) {
    Block block = accessor.GetBlock(pos);
    if (block.Id == 0)
      return false;
    Cuboidf[]? boxes = block.GetCollisionBoxes(accessor, pos);
    if (boxes == null)
      return false;
    (int iu, int iv) = PlaneAxes(a.Axis);
    int ia = 3 - iu - iv;
    double lo = AxisCoord(a.Cell, a.Axis) + 0.5 - WidthBlocks / 2;
    double hi = lo + WidthBlocks;
    double ra = Radius(a);
    double rb = Radius(b);
    double au = Comp(a.Cell, iu) + 0.5;
    double av = Comp(a.Cell, iv) + 0.5;
    double bu = Comp(b.Cell, iu) + 0.5;
    double bv = Comp(b.Cell, iv) + 0.5;
    double cu = Comp(pos, iu);
    double cv = Comp(pos, iv);
    double ca = Comp(pos, ia);
    foreach (Cuboidf box in boxes)
      if (
        Comp(box, ia + 3) + ca > lo + Eps
        && Comp(box, ia) + ca < hi - Eps
        && InLoop(
          au,
          av,
          ra,
          bu,
          bv,
          rb,
          cu + Comp(box, iu),
          cv + Comp(box, iv),
          cu + Comp(box, iu + 3),
          cv + Comp(box, iv + 3)
        )
      )
        return true;
    return false;
  }

  // Whether the rectangle's inside meets the hull of the rim circles; ternary search on the convex gap to the sweep.
  private static bool InLoop(
    double au,
    double av,
    double ra,
    double bu,
    double bv,
    double rb,
    double minU,
    double minV,
    double maxU,
    double maxV
  ) {
    double Gap(double t) {
      double pu = au + t * (bu - au);
      double pv = av + t * (bv - av);
      double du = Math.Max(Math.Max(minU - pu, pu - maxU), 0);
      double dv = Math.Max(Math.Max(minV - pv, pv - maxV), 0);
      return Math.Sqrt(du * du + dv * dv) - (ra + t * (rb - ra));
    }
    double lo = 0;
    double hi = 1;
    for (int i = 0; i < 80; i++) {
      double m1 = lo + (hi - lo) / 3;
      double m2 = hi - (hi - lo) / 3;
      if (Gap(m1) < Gap(m2))
        hi = m2;
      else
        lo = m1;
    }
    return Gap((lo + hi) / 2) < -Eps;
  }

  private static double Radius(BeltPulley p) =>
    MpBeltWear.PulleyDiameter(p.Size) / 2;

  private static double Distance(BeltPulley a, BeltPulley b) {
    (int iu, int iv) = PlaneAxes(a.Axis);
    double du = Comp(a.Cell, iu) - Comp(b.Cell, iu);
    double dv = Comp(a.Cell, iv) - Comp(b.Cell, iv);
    return Math.Sqrt(du * du + dv * dv);
  }

  private static (int U, int V) PlaneAxes(EnumAxis axis) =>
    axis switch {
      EnumAxis.X => (1, 2),
      EnumAxis.Y => (0, 2),
      _ => (0, 1),
    };

  private static int AxisCoord(BlockPos p, EnumAxis axis) => Comp(p, (int)axis);

  private static int Comp(BlockPos p, int i) =>
    i switch {
      0 => p.X,
      1 => p.Y,
      _ => p.Z,
    };

  private static double Comp(Vec3d p, int i) =>
    i switch {
      0 => p.X,
      1 => p.Y,
      _ => p.Z,
    };

  // Indices 0 to 2 the box's minimum corner, 3 to 5 its maximum.
  private static double Comp(Cuboidf box, int i) =>
    i switch {
      0 => box.X1,
      1 => box.Y1,
      2 => box.Z1,
      3 => box.X2,
      4 => box.Y2,
      _ => box.Z2,
    };

  private static Vec3d World(BeltPulley pulley, double u, double v) {
    (int iu, int iv) = PlaneAxes(pulley.Axis);
    var p = new double[3];
    p[iu] = u;
    p[iv] = v;
    p[3 - iu - iv] = AxisCoord(pulley.Cell, pulley.Axis) + 0.5;
    return new Vec3d(p[0], p[1], p[2]);
  }

  private static BlockPos At(BeltPulley pulley, int u, int v) {
    (int iu, int iv) = PlaneAxes(pulley.Axis);
    var p = new int[3];
    p[iu] = u;
    p[iv] = v;
    p[3 - iu - iv] = AxisCoord(pulley.Cell, pulley.Axis);
    return new BlockPos(p[0], p[1], p[2], pulley.Cell.dimension);
  }

  // Whether the segment runs through the inside of the rectangle for a positive length.
  private static bool Crosses(
    double su,
    double sv,
    double eu,
    double ev,
    double minU,
    double minV,
    double maxU,
    double maxV
  ) {
    double t0 = 0;
    double t1 = 1;
    double du = eu - su;
    double dv = ev - sv;
    if (
      !Clip(-du, su - minU, ref t0, ref t1)
      || !Clip(du, maxU - su, ref t0, ref t1)
      || !Clip(-dv, sv - minV, ref t0, ref t1)
      || !Clip(dv, maxV - sv, ref t0, ref t1)
      || t1 - t0 <= Eps
    )
      return false;
    double tm = (t0 + t1) / 2;
    double mu = su + tm * du;
    double mv = sv + tm * dv;
    return mu > minU + Eps
      && mu < maxU - Eps
      && mv > minV + Eps
      && mv < maxV - Eps;
  }

  private static bool Clip(double p, double q, ref double t0, ref double t1) {
    if (p == 0)
      return q >= 0;
    double r = q / p;
    if (p < 0) {
      if (r > t1)
        return false;
      if (r > t0)
        t0 = r;
    } else {
      if (r < t0)
        return false;
      if (r < t1)
        t1 = r;
    }
    return true;
  }
}
