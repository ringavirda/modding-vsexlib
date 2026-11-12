using System;
using System.Linq;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Storage;

/// <summary>
/// The one rule that lays an item on the ground and on a shelf from its own bounds: its
/// <see cref="StorageClass"/>, its pile and the transforms that seat its mesh. Every length is in
/// voxels (16 is one block) unless a member says blocks.
/// </summary>
public static class ExStorage {
  /// <summary>Longest posed extent of an item that lies in a cell by itself.</summary>
  public const double OneBlock = 17;

  /// <summary>Longest posed extent of an item set down singly, overhanging its cell.</summary>
  public const double LongestSingle = 24;

  /// <summary>Most items a pile holds.</summary>
  public const int PileCap = 64;

  /// <summary>Items in one loose heap.</summary>
  public const int LooseHeap = 12;

  /// <summary>The reason of an exempt item longer than <see cref="LongestSingle"/>.</summary>
  public const string TooLongReason =
    "longer than 24 voxels, stays in inventories";

  /// <summary>The reason of an exempt stock piece longer than <see cref="OneBlock"/>.</summary>
  public const string RackReason =
    "longer than one block: the stock rack holds it";

  /// <summary>Degrees a leaning tool's top tilts toward the wall.</summary>
  public const float LeanDegrees = 16;

  private const double Eps = 1e-4;
  private const int AngleStep = 90;
  private const double Block = 16;

  /// <summary>Gives <paramref name="facts"/> its class and layout: the first row that matches, in the
  /// order Kept, Lean, Piece, Loose, Pile, Quadrants, Halves, Single, SingleLong, Exempt.</summary>
  public static StorageSpec Classify(StorageFacts facts) {
    if (facts.Kept)
      return new StorageSpec(StorageClass.Kept, null, 0, 0);
    if (facts.UnderTools)
      return new StorageSpec(StorageClass.Lean, "WallHalves", 2, 0);
    if (facts.Unplaceable != null)
      return Exempt(facts.Unplaceable);

    double a = facts.A;
    double b = facts.B;
    double y2 = facts.C / Block;
    if (facts.StockPiece) {
      if (a <= 8)
        return new StorageSpec(StorageClass.Piece, "Quadrants", 4, y2);
      if (a <= 16 && b <= 8)
        return new StorageSpec(StorageClass.Piece, "Halves", 2, y2);
      if (a <= OneBlock)
        return new StorageSpec(StorageClass.Piece, "SingleCenter", 1, y2);
      return Exempt(RackReason);
    }
    if (
      facts.LooseShape || (facts.MaxStack >= 64 && (a <= 6 || facts.Fastener))
    )
      return new StorageSpec(StorageClass.Loose, "Messy12", LooseHeap, 0.125);
    if (facts.MaxStack > 1 && a <= Block) {
      bool bar = a >= 2.5 * b && facts.Fill >= 0.6;
      bool plate = facts.C <= 2 && facts.Fill >= 0.85;
      if (bar || plate) {
        (int perLayer, int layers) = Pile(a, b, facts.C, bar);
        if (perLayer > 0 && layers > 0)
          return new StorageSpec(
            StorageClass.Pile,
            "Stacking",
            Math.Min(PileCap, perLayer * layers),
            y2,
            perLayer,
            1.0 / perLayer
          );
      }
    }
    if (a <= 8)
      return new StorageSpec(StorageClass.Quadrants, "Quadrants", 4, y2);
    if (a <= 16 && b <= 8)
      return new StorageSpec(StorageClass.Halves, "Halves", 2, y2);
    if (a <= OneBlock && b <= Block)
      return new StorageSpec(StorageClass.Single, "SingleCenter", 1, y2);
    if (a <= LongestSingle)
      return new StorageSpec(StorageClass.SingleLong, "SingleCenter", 1, y2);
    return Exempt(TooLongReason);
  }

  /// <summary>Whether <paramref name="def"/> already lists the GroundStorable behavior, plain or by
  /// type.</summary>
  public static bool HasGroundStorable(ExItemDef def) {
    var json = def.ToJson();
    return new[] { "behaviors", "behaviorsByType" }.Any(key =>
      json[key]
        ?.SelectTokens("$..name")
        .Any(n => (string?)n == "GroundStorable") == true
    );
  }

  private static StorageSpec Exempt(string reason) =>
    new(StorageClass.Exempt, null, 0, 0, Reason: reason);

  /// <summary>The items in one layer and the layers of a pile of items posed <paramref name="a"/> x
  /// <paramref name="b"/> x <paramref name="c"/>. A bar of <paramref name="a"/> over 8 sits
  /// <c>floor(16/b)</c> side by side; a shorter bar, <c>floor(16/a) * floor(16/b)</c>; a plate,
  /// <c>floor(16/a) * floor(16/b)</c>. Layers are <c>floor(16/c)</c>; 0 where nothing fits.</summary>
  public static (int PerLayer, int Layers) Pile(
    double a,
    double b,
    double c,
    bool bar
  ) {
    int perLayer =
      bar && a > 8 ? Whole(Block / b) : Whole(Block / a) * Whole(Block / b);
    return (perLayer, Whole(Block / c));
  }

  private static int Whole(double ratio) => (int)Math.Floor(ratio + Eps);

  /// <summary>Height in blocks of the collision box of a pile holding <paramref name="stackSize"/>
  /// items, as the game scales it: one layer's height times the layers in use.</summary>
  public static double BoxHeight(StorageSpec pile, int stackSize) =>
    pile.CollisionY2 * Math.Ceiling(pile.CbScaleYByLayer * stackSize - Eps);

  /// <summary>The smallest shelf slot that holds an item lying flat, as the vanilla
  /// <c>shelvable</c> value (<c>Quadrants</c>, <c>Halves</c>, <c>SingleCenter</c>), or null when none
  /// does.</summary>
  /// <param name="a">Longest posed extent.</param>
  /// <param name="b">Middle posed extent.</param>
  /// <param name="c">Thinnest posed extent, standing vertical.</param>
  /// <param name="quadrant">The slot box of Quadrants, in voxels within the shelf block.</param>
  /// <param name="half">The slot box of Halves.</param>
  /// <param name="single">The slot box of SingleCenter.</param>
  public static string? ShelfFor(
    double a,
    double b,
    double c,
    Cuboidf quadrant,
    Cuboidf half,
    Cuboidf single
  ) {
    if (Holds(quadrant, a, b, c))
      return "Quadrants";
    if (Holds(half, a, b, c))
      return "Halves";
    return Holds(single, a, b, c) ? "SingleCenter" : null;
  }

  private static bool Holds(Cuboidf slot, double a, double b, double c) {
    double dx = slot.X2 - slot.X1;
    double dz = slot.Z2 - slot.Z1;
    return a <= Math.Max(dx, dz) + Eps
      && b <= Math.Min(dx, dz) + Eps
      && c <= slot.Y2 - slot.Y1 + Eps;
  }

  /// <summary>The transform that lays an item flat in <paramref name="slot"/>: turned in quarter turns
  /// only, thinnest extent vertical, longest along the slot's longer horizontal side (X on a tie),
  /// centred on the slot in X and Z with its lowest point on the slot's floor. Scale 1, origin
  /// (0.5, 0, 0.5).</summary>
  /// <param name="bounds">The drawn shape's box, in voxels.</param>
  /// <param name="slot">The slot's box, in voxels, in the frame the game adds its slot offset to.</param>
  public static ModelTransform Pose(Cuboidf bounds, Cuboidf slot) {
    bool alongX = slot.X2 - slot.X1 >= slot.Z2 - slot.Z1;
    (double rx, double ry, double rz) = (0, 0, 0);
    double bestHeight = double.MaxValue;
    double bestLong = -1;
    foreach ((double x, double y, double z) in Turns()) {
      Cuboidf posed = Transformed(bounds, x, y, z, 1, 0, 0, 0);
      double height = posed.Y2 - posed.Y1;
      double along = alongX ? posed.X2 - posed.X1 : posed.Z2 - posed.Z1;
      if (
        height < bestHeight - Eps
        || (height < bestHeight + Eps && along > bestLong + Eps)
      ) {
        (rx, ry, rz) = (x, y, z);
        bestHeight = height;
        bestLong = along;
      }
    }
    Cuboidf at = Transformed(bounds, rx, ry, rz, 1, 0, 0, 0);
    return Build(
      rx,
      ry,
      rz,
      (slot.X1 + slot.X2) / 2 - (at.X1 + at.X2) / 2,
      slot.Y1 - at.Y1,
      (slot.Z1 + slot.Z2) / 2 - (at.Z1 + at.Z2) / 2
    );
  }

  /// <summary>The transform that stands an item against the wall behind its block: the longest side
  /// vertical, the thinnest toward the wall, the top tilted <see cref="LeanDegrees"/> toward it, the
  /// lowest point on the floor, the wall-side face on the wall plane, centred along the wall.</summary>
  /// <param name="bounds">The drawn shape's box, in voxels.</param>
  public static ModelTransform Lean(Cuboidf bounds) {
    (double rx, double ry, double rz) = (0, 0, 0);
    double bestHeight = -1;
    double bestDepth = double.MaxValue;
    foreach ((double x, double y, double z) in Turns()) {
      Cuboidf posed = Transformed(bounds, x, y, z, 1, 0, 0, 0);
      double height = posed.Y2 - posed.Y1;
      double depth = posed.Z2 - posed.Z1;
      if (
        height > bestHeight + Eps
        || (height > bestHeight - Eps && depth < bestDepth - Eps)
      ) {
        (rx, ry, rz) = (x, y, z);
        bestHeight = height;
        bestDepth = depth;
      }
    }
    rx -= LeanDegrees;
    Cuboidf at = Transformed(bounds, rx, ry, rz, 1, 0, 0, 0);
    return Build(rx, ry, rz, Block / 2 - (at.X1 + at.X2) / 2, -at.Y1, -at.Z1);
  }

  // Every quarter-turn combination, identity first, so a tie keeps the least turned.
  private static System.Collections.Generic.IEnumerable<(
    double X,
    double Y,
    double Z
  )> Turns() {
    int steps = 360 / AngleStep;
    for (int i = 0; i < steps; i++)
      for (int j = 0; j < steps; j++)
        for (int k = 0; k < steps; k++)
          yield return (i * AngleStep, j * AngleStep, k * AngleStep);
  }

  private static ModelTransform Build(
    double rx,
    double ry,
    double rz,
    double tx,
    double ty,
    double tz
  ) {
    ModelTransform t = new ModelTransform().EnsureDefaultValues();
    t.Rotation.Set((float)Wrap(rx), (float)Wrap(ry), (float)Wrap(rz));
    t.Translation.Set(
      (float)(tx / Block),
      (float)(ty / Block),
      (float)(tz / Block)
    );
    t.Origin.Set(0.5f, 0, 0.5f);
    t.Scale = 1;
    return t;
  }

  private static double Wrap(double degrees) {
    double d = degrees % 360;
    if (d > 180)
      d -= 360;
    else if (d <= -180)
      d += 360;
    return d;
  }

  /// <summary>The axis-aligned box <paramref name="bounds"/> occupies once the game applies
  /// <paramref name="transform"/> to a mesh (translate, rotate X then Y then Z about the origin,
  /// scale), in voxels.</summary>
  /// <param name="bounds">The drawn shape's box, in voxels.</param>
  /// <param name="transform">A transform whose translation is in blocks.</param>
  public static Cuboidf Posed(Cuboidf bounds, ModelTransform transform) =>
    Transformed(
      bounds,
      transform.Rotation.X,
      transform.Rotation.Y,
      transform.Rotation.Z,
      transform.ScaleXYZ.X,
      transform.Translation.X * Block,
      transform.Translation.Y * Block,
      transform.Translation.Z * Block,
      transform.Origin.X * Block,
      transform.Origin.Y * Block,
      transform.Origin.Z * Block
    );

  private static Cuboidf Transformed(
    Cuboidf bounds,
    double rx,
    double ry,
    double rz,
    double scale,
    double tx,
    double ty,
    double tz,
    double ox = Block / 2,
    double oy = 0,
    double oz = Block / 2
  ) {
    double[,] m = Multiply(Multiply(RotX(rx), RotY(ry)), RotZ(rz));
    double[] lo = [double.MaxValue, double.MaxValue, double.MaxValue];
    double[] hi = [double.MinValue, double.MinValue, double.MinValue];
    for (int corner = 0; corner < 8; corner++) {
      double[] v =
      [
        ((corner & 1) == 0 ? bounds.X1 : bounds.X2) - ox,
        ((corner & 2) == 0 ? bounds.Y1 : bounds.Y2) - oy,
        ((corner & 4) == 0 ? bounds.Z1 : bounds.Z2) - oz,
      ];
      double[] origin = [ox, oy, oz];
      double[] shift = [tx, ty, tz];
      for (int row = 0; row < 3; row++) {
        double p =
          origin[row]
          + shift[row]
          + scale * (m[row, 0] * v[0] + m[row, 1] * v[1] + m[row, 2] * v[2]);
        lo[row] = Math.Min(lo[row], p);
        hi[row] = Math.Max(hi[row], p);
      }
    }
    return new Cuboidf(
      (float)lo[0],
      (float)lo[1],
      (float)lo[2],
      (float)hi[0],
      (float)hi[1],
      (float)hi[2]
    );
  }

  private static double[,] RotX(double degrees) {
    (double s, double c) = Math.SinCos(degrees * Math.PI / 180);
    return new[,]
    {
      { 1, 0, 0 },
      { 0, c, -s },
      { 0, s, c },
    };
  }

  private static double[,] RotY(double degrees) {
    (double s, double c) = Math.SinCos(degrees * Math.PI / 180);
    return new[,]
    {
      { c, 0, s },
      { 0, 1, 0 },
      { -s, 0, c },
    };
  }

  private static double[,] RotZ(double degrees) {
    (double s, double c) = Math.SinCos(degrees * Math.PI / 180);
    return new[,]
    {
      { c, -s, 0 },
      { s, c, 0 },
      { 0, 0, 1 },
    };
  }

  private static double[,] Multiply(double[,] l, double[,] r) {
    var o = new double[3, 3];
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        for (int k = 0; k < 3; k++)
          o[i, j] += l[i, k] * r[k, j];
    return o;
  }
}
