using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static ExpandedLib.Helpers.CollectibleViews;
using static ExpandedLib.Helpers.ViewReferences;

namespace ExpandedLib.Helpers;

/// <summary>Computes the views of an item from its shape: the third-person hand transform and held clips of
/// the kind its drawn box gives it, copied from the vanilla item that kind follows.</summary>
public static class ItemViews {
  /// <summary>Computes the views of <paramref name="collectible"/>. The kind is its declared
  /// <c>attributes.itemViews.kind</c> or the one its drawn box gives; <c>attributes.itemViews.tip</c> names
  /// the working end of a longtool or tool as a sign and an axis letter ("+y"). The slot and ground views
  /// are the game's defaults for an item.</summary>
  /// <param name="api">The api of the side whose world is loaded.</param>
  /// <param name="collectible">The collectible to compute.</param>
  /// <returns>The views; null for a block, a collectible that declares kind <c>none</c>, one with no
  /// drawn box, and one that declares a block kind.</returns>
  /// <exception cref="InvalidOperationException">The declared kind is not a kind name in lower case, or a
  /// workpiece is computed in a world with no vanilla work item.</exception>
  /// <exception cref="ArgumentException">The declared tip is not a sign and one of x, y, z.</exception>
  public static ItemViewSet? Compute(
    ICoreAPI api,
    CollectibleObject collectible
  ) {
    if (collectible is not Item item || OptedOut(collectible))
      return null;
    ItemViewKind? kind = KindOf(api, collectible);
    if (kind == ItemViewKind.Workpiece)
      return Workpiece(api);
    ViewBox? box = BoxOf(api, collectible);
    if (kind == null || box == null)
      return null;

    string? tip = item.Attributes?["itemViews"]?["tip"]?.AsString();
    if (kind == ItemViewKind.Tool) {
      Item? carrier = CarrierOf(api, item);
      HandReference? carried =
        carrier == null ? null : FromCarrier(api, carrier);
      if (carried != null)
        return Held(kind.Value, box, tip, carried);
      kind = ByBox(box.Extents(), false, false);
    }
    HandReference? reference = kind switch {
      ItemViewKind.Longtool => Spear,
      ItemViewKind.Plate => MetalPlate,
      ItemViewKind.Bulk or ItemViewKind.Lump => Ingot,
      ItemViewKind.Bar => Rod,
      _ => null,
    };
    return reference == null ? null : Held(kind.Value, box, tip, reference);
  }

  /// <summary>The vanilla item whose views a tool follows: among the game's items carrying the same <c>tool</c>
  /// value, the shortest code beginning with the value and a dash, else the first code; null when none carries it.</summary>
  internal static Item? CarrierOf(ICoreAPI api, Item item) {
    if (item.Tool is not { } tool)
      return null;
    string prefix = tool.ToString().ToLowerInvariant() + "-";
    Item[] carriers =
    [
      .. api.World.Items.Where(i =>
        i?.Code != null
        && i != item
        && i.Code.Domain == "game"
        && i.Tool == tool
      ),
    ];
    Item[] named =
    [
      .. carriers.Where(i =>
        i.Code.Path.StartsWith(prefix, StringComparison.Ordinal)
      ),
    ];
    return (named.Length > 0 ? named : carriers)
      .OrderBy(i => named.Length > 0 ? i.Code.Path.Length : 0)
      .ThenBy(i => i.Code.Path, StringComparer.Ordinal)
      .FirstOrDefault();
  }

  private static ItemViewSet Workpiece(ICoreAPI api) {
    Item reference = api
      .World.Items.Where(i => i is ItemWorkItem && i.Code?.Domain == "game")
      .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal)
      .First();
    return new ItemViewSet(
      ItemViewKind.Workpiece,
      reference.GuiTransform?.Clone() ?? ModelTransform.ItemDefaultGui(),
      reference.TpHandTransform?.Clone() ?? ModelTransform.ItemDefaultTp(),
      reference.GroundTransform?.Clone() ?? ModelTransform.ItemDefaultGround(),
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null
    );
  }

  // The hand reference a carrier's shipped views make: its grip is the point of its shape that its hand
  // transform puts on the hand.
  private static HandReference? FromCarrier(ICoreAPI api, Item carrier) {
    ModelTransform? tf = carrier.TpHandTransform;
    ViewBox? box = BoxOf(api, carrier);
    if (tf == null || box == null)
      return null;
    Lay lay = Lay.Of(box, true, null);
    double s = tf.ScaleXYZ.X;
    double[,] r = Rotation(
      new Vec3d(tf.Rotation.X, tf.Rotation.Y - HandYaw, tf.Rotation.Z)
    );
    var back = new double[3, 3];
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        back[i, j] = r[j, i];
    Vec3d o = new(tf.Origin.X, tf.Origin.Y, tf.Origin.Z);
    Vec3d q = Mul(
      back,
      new Vec3d(
        -o.X / s - tf.Translation.X,
        -o.Y / s - tf.Translation.Y,
        -o.Z / s - tf.Translation.Z
      )
    );
    var grip = new Vec3d((o.X + q.X) * 16, (o.Y + q.Y) * 16, (o.Z + q.Z) * 16);
    double[] u = lay.Coords(grip);
    box.Nearest(grip, out double off);
    return new HandReference(
      lay.Axes,
      lay.Extent(2),
      new Vec3d(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z),
      s,
      [.. u.Select((v, k) => (v - lay.Lo[k]) / lay.Extent(k))],
      off
    );
  }

  private static ItemViewSet Held(
    ItemViewKind kind,
    ViewBox box,
    string? tip,
    HandReference reference
  ) {
    Lay lay = Lay.Of(
      box,
      kind is ItemViewKind.Longtool or ItemViewKind.Tool,
      tip
    );
    var turn = new double[3, 3];
    for (int k = 0; k < 3; k++)
      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
          turn[i, j] +=
            Lay.Component(reference.Axes[k], i) * Lay.Component(lay.Axes[k], j);
    bool mirrored = Det(turn) < 0;
    if (mirrored)
      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
          turn[i, j] -=
            2
            * Lay.Component(reference.Axes[2], i)
            * Lay.Component(lay.Axes[2], j);

    double[,] r = Mul(
      Rotation(
        new Vec3d(
          reference.Rotation.X,
          reference.Rotation.Y - HandYaw,
          reference.Rotation.Z
        )
      ),
      turn
    );
    Vec3d euler = Euler(r);
    Vec3d grip =
      kind == ItemViewKind.Bulk
        ? BulkGrip(lay, box)
        : Grip(lay, box, reference, mirrored);
    double s = reference.Scale;
    ModelTransform hand = new() {
      Translation = new FastVec3f(
        (float)(-grip.X / 16 / s),
        (float)(-grip.Y / 16 / s),
        (float)(-grip.Z / 16 / s)
      ),
      Rotation = new FastVec3f(
        (float)Wrap(euler.X),
        (float)Wrap(euler.Y + HandYaw),
        (float)Wrap(euler.Z)
      ),
      Origin = new FastVec3f(
        (float)(grip.X / 16),
        (float)(grip.Y / 16),
        (float)(grip.Z / 16)
      ),
      Scale = (float)s,
      Rotate = true,
    };
    string? idle = kind switch {
      ItemViewKind.Longtool => SpearIdle,
      ItemViewKind.Bulk => BulkHold,
      _ => null,
    };
    return new ItemViewSet(
      kind,
      ModelTransform.ItemDefaultGui(),
      hand,
      ModelTransform.ItemDefaultGround(),
      idle,
      idle,
      kind == ItemViewKind.Longtool ? SpearHit : null,
      null,
      new Vec3d(lay.Lo[0], lay.Lo[1], lay.Lo[2]),
      new Vec3d(lay.Hi[0], lay.Hi[1], lay.Hi[2]),
      lay.Axes,
      grip
    );
  }

  // Fractions along the lay's two longer axes, a clearance across the shortest; a point farther
  // from the drawn elements than the reference's own is moved onto the nearest drawn point.
  private static Vec3d Grip(
    Lay lay,
    ViewBox box,
    HandReference reference,
    bool mirrored
  ) {
    double[] u = new double[3];
    for (int k = 0; k < 2; k++)
      u[k] = lay.Lo[k] + reference.Grip[k] * lay.Extent(k);
    double f = reference.Grip[2];
    bool referenceTop = f >= 0.5;
    double clearance = referenceTop
      ? (f - 1) * reference.Thickness
      : -f * reference.Thickness;
    u[2] =
      referenceTop != mirrored ? lay.Hi[2] + clearance : lay.Lo[2] - clearance;
    Vec3d grip = lay.Point(u);
    Vec3d near = box.Nearest(grip, out double distance);
    return distance > reference.OffDrawn + 1e-6 ? near : grip;
  }

  private static Vec3d BulkGrip(Lay lay, ViewBox box) =>
    box.Nearest(
      lay.Point([.. lay.Lo.Select((lo, k) => (lo + lay.Hi[k]) / 2)]),
      out _
    );

  private static double Det(double[,] m) =>
    m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
    - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
    + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

  // The angles in degrees whose Rx * Ry * Rz is r.
  private static Vec3d Euler(double[,] r) {
    double b = Math.Asin(Math.Clamp(r[0, 2], -1, 1));
    double a,
      c = 0;
    if (Math.Abs(r[0, 2]) < 1 - 1e-9) {
      a = Math.Atan2(-r[1, 2], r[2, 2]);
      c = Math.Atan2(-r[0, 1], r[0, 0]);
    } else {
      a = Math.Atan2(r[2, 1], r[1, 1]);
    }
    return new Vec3d(a * 180 / Math.PI, b * 180 / Math.PI, c * 180 / Math.PI);
  }

  private static double Wrap(double degrees) =>
    degrees - 360 * Math.Round(degrees / 360);
}
