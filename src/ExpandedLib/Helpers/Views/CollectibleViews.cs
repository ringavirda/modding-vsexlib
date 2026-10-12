using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>The box a collectible's drawn faces fill, and the kind the box gives it.</summary>
internal static class CollectibleViews {
  // Keyed by shape path, selection and rotation. A null entry is a shape that does not load or has no drawn face.
  private static readonly Dictionary<string, ViewBox?> Boxes = new();

  // The centre of a 16-voxel block: the pivot a composite shape's rotation turns about.
  private static readonly Vec3d Centre = new(8, 8, 8);

  /// <summary>Empties the box cache; a world starting to load empties it through ExWorldState.</summary>
  internal static void ResetForWorld() => Boxes.Clear();

  /// <summary>The drawn box of <paramref name="collectible"/>, in voxels; null for a texture-only item, a
  /// shape that does not load, or a shape with no drawn face.</summary>
  internal static ViewBox? BoxOf(ICoreAPI api, CollectibleObject collectible) =>
    collectible is Block block
      ? BlockBox(api, block)
      : ItemBox(api, ((Item)collectible).Shape);

  /// <summary>The kind of <paramref name="collectible"/>: its declared kind when it has one, else the first kind
  /// of the table its box and fields match. Null when there is no box to classify.</summary>
  /// <exception cref="InvalidOperationException">The declared kind is not a kind name in lower case.</exception>
  internal static ItemViewKind? KindOf(
    ICoreAPI api,
    CollectibleObject collectible
  ) {
    ItemViewKind declared = Declared(collectible);
    if (declared != ItemViewKind.None)
      return declared;
    if (collectible is ItemWorkItem)
      return ItemViewKind.Workpiece;
    ViewBox? box = BoxOf(api, collectible);
    return box == null
      ? null
      : ByBox(box.Extents(), collectible is Block, collectible.Tool != null);
  }

  /// <summary>The kind of a box whose side lengths are <paramref name="extents"/>, longest first, in voxels.</summary>
  /// <param name="extents">The longest, middle and shortest side of the box.</param>
  /// <param name="block">Whether the box is a block's; blocks take only the three block kinds.</param>
  /// <param name="tool">Whether the item has a <c>tool</c> field.</param>
  internal static ItemViewKind ByBox(double[] extents, bool block, bool tool) {
    double l = extents[0],
      m = extents[1],
      s = extents[2];
    if (block)
      return l > 16.5 ? ItemViewKind.Multicell
        : extents.All(x => x >= 15) ? ItemViewKind.Cube
        : ItemViewKind.Partial;
    if (l >= 32 && m <= l / 6)
      return ItemViewKind.Longtool;
    if (tool)
      return ItemViewKind.Tool;
    if (s <= m / 3 && m >= l / 2)
      return ItemViewKind.Plate;
    if (l < 3 * m && m >= 10)
      return ItemViewKind.Bulk;
    return l >= 3 * m ? ItemViewKind.Bar : ItemViewKind.Lump;
  }

  /// <summary>A box in voxels: <paramref name="From"/> and <paramref name="To"/> are its corners in the frame
  /// whose axes are <paramref name="Axes"/>, the unit vectors of the shape's frame.</summary>
  internal sealed record ViewBox(Vec3d From, Vec3d To, Vec3d[] Axes) {
    /// <summary>The three side lengths in voxels, longest first.</summary>
    internal double[] Extents() {
      double[] e = [To.X - From.X, To.Y - From.Y, To.Z - From.Z];
      Array.Sort(e);
      Array.Reverse(e);
      return e;
    }
  }

  private static ItemViewKind Declared(CollectibleObject collectible) {
    string? name = collectible.Attributes?["itemViews"]?["kind"]?.AsString();
    if (name == null)
      return ItemViewKind.None;
    foreach (ItemViewKind kind in Enum.GetValues<ItemViewKind>())
      if (kind.ToString().ToLowerInvariant() == name)
        return kind;
    throw new InvalidOperationException(
      $"{collectible.Code}: attributes.itemViews.kind '{name}' is not an item view kind in lower case"
    );
  }

  private static ViewBox? ItemBox(ICoreAPI api, CompositeShape? shape) =>
    shape == null
      ? null
      : Measure(
        api,
        shape.Base.ToString(),
        shape.SelectiveElements,
        new Vec3d()
      );

  private static ViewBox? BlockBox(ICoreAPI api, Block block) {
    CompositeShape? shape = block.ShapeInventory ?? block.Shape;
    return Measure(
      api,
      shape?.Base.ToString() ?? "game:block/basic/cube",
      shape?.SelectiveElements,
      shape == null
        ? new Vec3d()
        : new Vec3d(shape.rotateX, shape.rotateY, shape.rotateZ)
    );
  }

  private static ViewBox? Measure(
    ICoreAPI api,
    string basePath,
    string[]? keep,
    Vec3d rotation
  ) {
    string key =
      $"{basePath}|{string.Join(',', keep ?? [])}|{rotation.X}|{rotation.Y}|{rotation.Z}";
    if (Boxes.TryGetValue(key, out ViewBox? cached))
      return cached;
    Shape? shape = ExMeshCache.LoadShape(
      api,
      new AssetLocation(basePath)
        .WithPathPrefixOnce("shapes/")
        .WithPathAppendixOnce(".json")
    );
    ViewBox? box =
      shape == null ? null : Fit(Drawn(shape, keep), Pose.Turn(rotation));
    Boxes[key] = box;
    return box;
  }

  // A drawn element has an enabled face with a texture and at least two sides of length. A disabled
  // face is absent from FacesResolved, and the '#' of "#null" is dropped on load, so the texture reads "null".
  private static bool IsDrawn(ShapeElement el) {
    Vec3d from = Vec(el.From);
    Vec3d to = Vec(el.To);
    int sides = new[] { to.X - from.X, to.Y - from.Y, to.Z - from.Z }.Count(x =>
      Math.Abs(x) > 1e-6
    );
    return sides >= 2
      && el.FacesResolved?.Any(f => f is { Texture: not null and not "null" })
        == true;
  }

  private static List<Element> Drawn(Shape shape, string[]? keep) {
    var drawn = new List<Element>();
    Walk(shape.Elements, Pose.Identity, "", keep, drawn);
    return drawn;
  }

  private static void Walk(
    ShapeElement[]? elements,
    Pose parent,
    string parentPath,
    string[]? keep,
    List<Element> drawn
  ) {
    foreach (ShapeElement el in elements ?? []) {
      string path =
        parentPath.Length == 0 ? el.Name ?? "" : parentPath + "/" + el.Name;
      if (keep is { Length: > 0 } && !ExShapeElements.Matches(path, keep))
        continue;
      Pose pose = parent.Then(Local(el));
      if (IsDrawn(el))
        drawn.Add(new Element(Corners(el, pose), pose.R));
      Walk(el.Children, pose, path, keep, drawn);
    }
  }

  private static Pose Local(ShapeElement el) {
    double[,] r = Rotation(new Vec3d(el.RotationX, el.RotationY, el.RotationZ));
    Vec3d origin = Vec(el.RotationOrigin);
    return new Pose(r, origin - Mul(r, origin));
  }

  private static Vec3d[] Corners(ShapeElement el, Pose pose) {
    Vec3d from = Vec(el.From);
    Vec3d to = Vec(el.To);
    var corners = new Vec3d[8];
    for (int i = 0; i < 8; i++)
      corners[i] = pose.Apply(
        new Vec3d(
          (i & 1) == 0 ? from.X : to.X,
          (i & 2) == 0 ? from.Y : to.Y,
          (i & 4) == 0 ? from.Z : to.Z
        )
      );
    return corners;
  }

  // The frame among the identity and each drawn element's own rotation (after the turn) with the least
  // volume; a tie keeps the earlier frame, so the identity wins a tie.
  private static ViewBox? Fit(List<Element> drawn, Pose turn) {
    if (drawn.Count == 0)
      return null;
    var frames = new List<double[,]> { Identity };
    var points = new List<Vec3d>();
    foreach (Element e in drawn) {
      points.AddRange(e.Corners.Select(turn.Apply));
      double[,] frame = Mul(turn.R, e.Frame);
      if (frames.All(g => !SameFrame(g, frame)))
        frames.Add(frame);
    }

    ViewBox? best = null;
    double bestVolume = double.PositiveInfinity;
    foreach (double[,] frame in frames) {
      Vec3d[] projected = points.Select(p => Project(frame, p)).ToArray();
      Vec3d from = new(
        projected.Min(v => v.X),
        projected.Min(v => v.Y),
        projected.Min(v => v.Z)
      );
      Vec3d to = new(
        projected.Max(v => v.X),
        projected.Max(v => v.Y),
        projected.Max(v => v.Z)
      );
      double volume = (to.X - from.X) * (to.Y - from.Y) * (to.Z - from.Z);
      if (volume < bestVolume - 1e-6) {
        bestVolume = volume;
        best = new ViewBox(
          from,
          to,
          [
            new Vec3d(frame[0, 0], frame[1, 0], frame[2, 0]),
            new Vec3d(frame[0, 1], frame[1, 1], frame[2, 1]),
            new Vec3d(frame[0, 2], frame[1, 2], frame[2, 2]),
          ]
        );
      }
    }
    return best;
  }

  private static bool SameFrame(double[,] a, double[,] b) {
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        if (Math.Abs(a[i, j] - b[i, j]) > 1e-3)
          return false;
    return true;
  }

  private static Vec3d Project(double[,] frame, Vec3d p) =>
    new(
      frame[0, 0] * p.X + frame[1, 0] * p.Y + frame[2, 0] * p.Z,
      frame[0, 1] * p.X + frame[1, 1] * p.Y + frame[2, 1] * p.Z,
      frame[0, 2] * p.X + frame[1, 2] * p.Y + frame[2, 2] * p.Z
    );

  private static Vec3d Vec(double[]? a) =>
    a == null ? new Vec3d() : new Vec3d(a[0], a[1], a[2]);

  private static readonly double[,] Identity =
  {
    { 1, 0, 0 },
    { 0, 1, 0 },
    { 0, 0, 1 },
  };

  // Rx * Ry * Rz with angles in degrees, column vectors: the order the shape format composes its rotations in.
  private static double[,] Rotation(Vec3d degrees) =>
    Mul(Mul(RotationX(degrees.X), RotationY(degrees.Y)), RotationZ(degrees.Z));

  private static double[,] RotationX(double degrees) {
    double a = degrees * Math.PI / 180,
      c = Math.Cos(a),
      s = Math.Sin(a);
    return new[,]
    {
      { 1, 0, 0 },
      { 0, c, -s },
      { 0, s, c },
    };
  }

  private static double[,] RotationY(double degrees) {
    double a = degrees * Math.PI / 180,
      c = Math.Cos(a),
      s = Math.Sin(a);
    return new[,]
    {
      { c, 0, s },
      { 0, 1, 0 },
      { -s, 0, c },
    };
  }

  private static double[,] RotationZ(double degrees) {
    double a = degrees * Math.PI / 180,
      c = Math.Cos(a),
      s = Math.Sin(a);
    return new[,]
    {
      { c, -s, 0 },
      { s, c, 0 },
      { 0, 0, 1 },
    };
  }

  private static double[,] Mul(double[,] a, double[,] b) {
    var r = new double[3, 3];
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        for (int k = 0; k < 3; k++)
          r[i, j] += a[i, k] * b[k, j];
    return r;
  }

  private static Vec3d Mul(double[,] m, Vec3d v) =>
    new(
      m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z,
      m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z,
      m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z
    );

  // A rigid map taking x to R x + T, in shape voxels.
  private readonly record struct Pose(double[,] R, Vec3d T) {
    internal static Pose Identity =>
      new(CollectibleViews.Identity, new Vec3d());

      internal static Pose Turn(Vec3d degrees) {
      double[,] r = Rotation(degrees);
      return new Pose(r, Centre - Mul(r, Centre));
    }

    internal Vec3d Apply(Vec3d p) => Mul(R, p) + T;

      internal Pose Then(Pose child) => new(Mul(R, child.R), Apply(child.T));
  }

  private readonly record struct Element(Vec3d[] Corners, double[,] Frame);
}
