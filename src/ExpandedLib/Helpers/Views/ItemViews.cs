using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static ExpandedLib.Helpers.CollectibleViews;
using static ExpandedLib.Helpers.ViewReferences;

namespace ExpandedLib.Helpers;

/// <summary>Computes the views of an item from its shape: the slot, third-person hand and ground transforms and
/// the held clips of the kind its drawn box gives it, copied from the vanilla item that kind follows.</summary>
public static class ItemViews {
  /// <summary>Computes the views of <paramref name="collectible"/>. The kind is its declared
  /// <c>attributes.itemViews.kind</c> or the one its drawn box gives; <c>attributes.itemViews.tip</c> names
  /// the working end of a longtool or tool as a sign and an axis letter ("+y"). The slot and ground views
  /// are computed from the box as well. A block takes the game's block rotations with its origins on the drawn box.
  /// A mod item with no <c>attributes.itemViews</c> key that draws a game item's shape takes that item's shipped views.</summary>
  /// <param name="api">The api of the side whose world is loaded.</param>
  /// <param name="collectible">The collectible to compute.</param>
  /// <returns>The views; null for a collectible that declares kind <c>none</c>, one with no drawn box, an item
  /// that declares a block kind and a block that declares an item kind.</returns>
  /// <exception cref="InvalidOperationException">The declared kind is not a kind name in lower case, or a
  /// workpiece is computed in a world with no vanilla work item.</exception>
  /// <exception cref="ArgumentException">The declared tip is not a sign and one of x, y, z.</exception>
  public static ItemViewSet? Compute(
    ICoreAPI api,
    CollectibleObject collectible
  ) {
    if (OptedOut(collectible))
      return null;
    ItemViewKind? kind = KindOf(api, collectible);
    ViewBox? box = BoxOf(api, collectible);
    if (collectible is Block) {
      return
        kind
          is ItemViewKind.Cube
            or ItemViewKind.Partial
            or ItemViewKind.Multicell
        && box != null
        ? ViewBlocks.Compute(kind.Value, box)
        : null;
    }
    if (collectible is not Item item)
      return null;
    if (kind == ItemViewKind.Workpiece)
      return Workpiece(api);
    if (kind == null || box == null)
      return null;
    if (VanillaShape(api, item) is { } twin)
      return Shipped(api, item, twin, box);

    string? tip = item.Attributes?["itemViews"]?["tip"]?.AsString();
    if (kind == ItemViewKind.Tool) {
      Item? carrier = CarrierOf(api, item);
      HandReference? carried =
        carrier == null ? null : FromCarrier(api, carrier);
      if (carried != null)
        return Held(kind.Value, box, tip, carried);
      kind = ByBox(box.Extents(), false, false);
    }
    HandReference? reference = ReferenceOf(kind.Value);
    return reference == null ? null : Held(kind.Value, box, tip, reference);
  }

  /// <summary>Computes the views of an item drawn as <paramref name="shape"/> and laid out as
  /// <paramref name="kind"/>, for a mod that wants the numbers without a collectible.</summary>
  /// <param name="api">The api of the side whose world is loaded.</param>
  /// <param name="shape">The shape's base, selectiveElements and rotateX/Y/Z are read.</param>
  /// <param name="kind">Longtool, Plate, Bulk, Bar or Lump.</param>
  /// <param name="tip">The working end of a longtool as a sign and an axis letter ("+y"), or null.</param>
  /// <returns>The views, as the collectible overload gives an item of that shape and kind.</returns>
  /// <exception cref="ArgumentException"><paramref name="kind"/> is None, Tool, Workpiece or a block kind,
  /// whose reference is a collectible or a block; or <paramref name="tip"/> is not a sign and one of x, y, z.</exception>
  /// <exception cref="InvalidOperationException">The shape does not load or has no drawn face.</exception>
  public static ItemViewSet Compute(
    ICoreAPI api,
    CompositeShape shape,
    ItemViewKind kind,
    string? tip = null
  ) {
    HandReference reference =
      ReferenceOf(kind)
      ?? throw new ArgumentException(
        $"kind {kind} has no fixed reference to lay a shape out against",
        nameof(kind)
      );
    ViewBox box =
      ItemBox(api, shape)
      ?? throw new InvalidOperationException(
        $"shape {shape.Base} does not load or has no drawn face"
      );
    return Held(kind, box, tip, reference);
  }

  private static HandReference? ReferenceOf(ItemViewKind kind) =>
    kind switch {
      ItemViewKind.Longtool => Spear,
      ItemViewKind.Plate => MetalPlate,
      ItemViewKind.Bulk or ItemViewKind.Lump => Ingot,
      ItemViewKind.Bar => Rod,
      _ => null,
    };

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

  // The game item that draws the same shape as a mod item with no declared kind or tip; the first by
  // ordinal code. A game item has none.
  private static Item? VanillaShape(ICoreAPI api, Item item) {
    if (
      item.Code?.Domain == "game"
      || item.Shape?.Base == null
      || item.Attributes?["itemViews"].Exists == true
    )
      return null;
    string[] keep = item.Shape.SelectiveElements ?? [];
    return api
      .World.Items.Where(i =>
        i?.Code?.Domain == "game"
        && i.Shape?.Base != null
        && i.Shape.Base.Equals(item.Shape.Base)
        && keep.SequenceEqual(i.Shape.SelectiveElements ?? [])
      )
      .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal)
      .FirstOrDefault();
  }

  // The shipped views of a game item, for a mod item that draws its shape.
  private static ItemViewSet Shipped(
    ICoreAPI api,
    Item item,
    Item twin,
    ViewBox box
  ) {
    ModelTransform hand =
      twin.TpHandTransform?.Clone() ?? ModelTransform.ItemDefaultTp();
    Lay lay = Lay.Of(box, true, null);
    return new ItemViewSet(
      ByBox(box.Extents(), false, false),
      twin.GuiTransform?.Clone() ?? ModelTransform.ItemDefaultGui(),
      hand,
      twin.GroundTransform?.Clone() ?? ModelTransform.ItemDefaultGround(),
      twin.HeldRightTpIdleAnimation,
      twin.HeldRightReadyAnimation == DefaultReady
        ? null
        : twin.HeldRightReadyAnimation,
      twin.HeldTpHitAnimation == DefaultHit ? null : twin.HeldTpHitAnimation,
      twin.HeldTpUseAnimation == DefaultUse ? null : twin.HeldTpUseAnimation,
      new Vec3d(lay.Lo[0], lay.Lo[1], lay.Lo[2]),
      new Vec3d(lay.Hi[0], lay.Hi[1], lay.Hi[2]),
      lay.Axes,
      HandGrip(hand)
    );
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

  // The point of the shape, in voxels, that a hand transform puts on the hand.
  private static Vec3d HandGrip(ModelTransform tf) {
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
    return new Vec3d((o.X + q.X) * 16, (o.Y + q.Y) * 16, (o.Z + q.Z) * 16);
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
    Vec3d grip = HandGrip(tf);
    double[] u = lay.Coords(grip);
    box.Nearest(grip, out double off);
    return new HandReference(
      lay.Axes,
      lay.Extent(2),
      new Vec3d(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z),
      s,
      [.. u.Select((v, k) => (v - lay.Lo[k]) / lay.Extent(k))],
      off,
      carrier.GuiTransform == null
        ? new Vec3d()
        : new Vec3d(
          carrier.GuiTransform.Rotation.X,
          carrier.GuiTransform.Rotation.Y,
          carrier.GuiTransform.Rotation.Z
        ),
      carrier.GroundTransform?.ScaleXYZ.X
        ?? ModelTransform.ItemDefaultGround().ScaleXYZ.X
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
      Slot(kind, lay, reference, turn),
      hand,
      Ground(lay, reference),
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

  // The reference's slot rotation after the lay's turn, the origin on the box centre (a longtool's on the
  // spear's close-up), and a translation that puts the origin on the slot centre.
  private static ModelTransform Slot(
    ItemViewKind kind,
    Lay lay,
    HandReference reference,
    double[,] turn
  ) {
    double[,] r = Mul(Rotation(reference.GuiRotation), turn);
    double[] mid = [.. lay.Lo.Select((lo, k) => (lo + lay.Hi[k]) / 2)];
    bool longtool = kind == ItemViewKind.Longtool;
    if (longtool)
      mid[0] = lay.Lo[0] + SpearSlotOrigin * lay.Extent(0);
    Vec3d o = lay.Point(mid);
    double reach = 0;
    for (int c = 0; c < 8; c++) {
      Vec3d corner = lay.Point([
        (c & 1) == 0 ? lay.Lo[0] : lay.Hi[0],
        (c & 2) == 0 ? lay.Lo[1] : lay.Hi[1],
        (c & 4) == 0 ? lay.Lo[2] : lay.Hi[2],
      ]);
      Vec3d v = Mul(
        r,
        new Vec3d(corner.X - o.X, corner.Y - o.Y, corner.Z - o.Z)
      );
      reach = Math.Max(reach, Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));
    }
    double span = 2 * reach / 16;
    double s = longtool
      ? SpearSlotScale * SpearLength / lay.Extent(0)
      : SlotSpan * SlotPixels / (SlotItemPixels * span);
    Vec3d euler = Euler(r);
    return new ModelTransform {
      Translation = new FastVec3f(
        (float)(SlotShiftX - o.X / 16),
        (float)(SlotShiftY - o.Y / 16),
        0
      ),
      Rotation = new FastVec3f(
        (float)Wrap(euler.X),
        (float)Wrap(euler.Y),
        (float)Wrap(euler.Z)
      ),
      Origin = new FastVec3f(
        (float)(o.X / 16),
        (float)(o.Y / 16),
        (float)(o.Z / 16)
      ),
      Scale = (float)s,
      Rotate = true,
    };
  }

  // The short axis vertical and the long axis along x, the origin on the box centre, resting on y 0.
  private static ModelTransform Ground(Lay lay, HandReference reference) {
    double handed =
      Det(
        new double[,]
        {
          { lay.Axes[0].X, lay.Axes[0].Y, lay.Axes[0].Z },
          { lay.Axes[1].X, lay.Axes[1].Y, lay.Axes[1].Z },
          { lay.Axes[2].X, lay.Axes[2].Y, lay.Axes[2].Z },
        }
      ) > 0
        ? -1
        : 1;
    double[,] target =
    {
      { 1, 0, 0 },
      { 0, 0, 1 },
      { 0, handed, 0 },
    };
    var r = new double[3, 3];
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        for (int k = 0; k < 3; k++)
          r[i, j] += target[i, k] * Lay.Component(lay.Axes[k], j);
    // The game turns about y, then z, then x: the angles of Ry * Rz * Rx are Euler's of the relabelled matrix.
    int[] next = [1, 2, 0];
    var relabelled = new double[3, 3];
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        relabelled[i, j] = r[next[i], next[j]];
    Vec3d euler = Euler(relabelled);
    Vec3d o = lay.Point([.. lay.Lo.Select((lo, k) => (lo + lay.Hi[k]) / 2)]);
    double s = reference.GroundScale;
    return new ModelTransform {
      Translation = new FastVec3f(
        0,
        (float)(GroundUnit * s * lay.Extent(2) / 32),
        0
      ),
      Rotation = new FastVec3f(
        (float)Wrap(euler.Z),
        (float)Wrap(euler.X),
        (float)Wrap(euler.Y)
      ),
      Origin = new FastVec3f(
        (float)(o.X / 16),
        (float)(o.Y / 16),
        (float)(o.Z / 16)
      ),
      Scale = (float)s,
      Rotate = true,
    };
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
