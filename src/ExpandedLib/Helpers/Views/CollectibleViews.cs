using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>The box a collectible's drawn faces fill, and the kind the box gives it.</summary>
internal static class CollectibleViews {
  // Keyed by shape path, selection and rotation. A null entry is a shape that does not load or has no drawn face.
  private static readonly ConcurrentDictionary<string, ViewBox?> Boxes = new();

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
  /// whose axes are <paramref name="Axes"/>, unit vectors in the shape's frame. <paramref name="Drawn"/> holds the
  /// eight corners of each drawn element in the shape's frame, corner i high in x, y, z by bits 0, 1, 2 of i.
  /// <paramref name="Faces"/> flags, per drawn element, its drawn faces in the order north, east, south, west, up, down.</summary>
  internal sealed record ViewBox(
    Vec3d From,
    Vec3d To,
    Vec3d[] Axes,
    IReadOnlyList<Vec3d[]> Drawn,
    IReadOnlyList<bool[]> Faces
  ) {
    /// <summary>The point of a drawn element's bounding box nearest <paramref name="p"/>, in the shape's
    /// frame, with its distance in voxels.</summary>
    internal Vec3d Nearest(Vec3d p, out double distance) {
      Vec3d best = p;
      distance = double.PositiveInfinity;
      foreach (Vec3d[] corners in Drawn) {
        var q = new Vec3d(
          Math.Clamp(p.X, corners.Min(c => c.X), corners.Max(c => c.X)),
          Math.Clamp(p.Y, corners.Min(c => c.Y), corners.Max(c => c.Y)),
          Math.Clamp(p.Z, corners.Min(c => c.Z), corners.Max(c => c.Z))
        );
        double d = q.DistanceTo(p);
        if (d < distance) {
          distance = d;
          best = q;
        }
      }
      return best;
    }

    /// <summary>The three side lengths in voxels, longest first.</summary>
    internal double[] Extents() {
      double[] e = [To.X - From.X, To.Y - From.Y, To.Z - From.Z];
      Array.Sort(e);
      Array.Reverse(e);
      return e;
    }
  }

  /// <summary>Whether <paramref name="collectible"/> declares <c>attributes.itemViews.kind</c> as
  /// <c>none</c>, which leaves its transforms to the game.</summary>
  internal static bool OptedOut(CollectibleObject collectible) =>
    collectible.Attributes?["itemViews"]?["kind"]?.AsString() == "none";

  /// <summary>The declared <c>attributes.itemViews.guiRotation</c> of <paramref name="collectible"/> in degrees
  /// about x, y and z; null when none is declared.</summary>
  /// <exception cref="ArgumentException">The declared value is not an array of three numbers.</exception>
  internal static Vec3d? GuiRotationOf(CollectibleObject collectible) {
    JsonObject? node = collectible.Attributes?["itemViews"]?["guiRotation"];
    if (node == null || !node.Exists)
      return null;
    if (
      node.Token is not JArray { Count: 3 } a
      || !a.All(t => t.Type is JTokenType.Integer or JTokenType.Float)
    )
      throw new ArgumentException(
        $"{collectible.Code}: attributes.itemViews.guiRotation is not an array of three numbers"
      );
    double[] v = a.Select(t => (double)t).ToArray();
    return new Vec3d(v[0], v[1], v[2]);
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

  /// <summary>The drawn box of <paramref name="shape"/> as an item draws it, in voxels; null for no shape, a
  /// shape that does not load, or one with no drawn face.</summary>
  internal static ViewBox? ItemBox(ICoreAPI api, CompositeShape? shape) =>
    shape == null
      ? null
      : Measure(
        api,
        shape.Base.ToString(),
        shape.SelectiveElements,
        new Vec3d(shape.rotateX, shape.rotateY, shape.rotateZ),
        false
      );

  private static ViewBox? BlockBox(ICoreAPI api, Block block) {
    CompositeShape? shape = block.ShapeInventory ?? block.Shape;
    return Measure(
      api,
      shape?.Base.ToString() ?? "game:block/basic/cube",
      shape?.SelectiveElements,
      shape == null
        ? new Vec3d()
        : new Vec3d(shape.rotateX, shape.rotateY, shape.rotateZ),
      true
    );
  }

  private static ViewBox? Measure(
    ICoreAPI api,
    string basePath,
    string[]? keep,
    Vec3d rotation,
    bool shapeFrame
  ) {
    string key =
      $"{basePath}|{string.Join(',', keep ?? [])}|{rotation.X}|{rotation.Y}|{rotation.Z}|{shapeFrame}";
    if (Boxes.TryGetValue(key, out ViewBox? cached))
      return cached;
    Shape? shape = ExMeshCache.LoadShape(
      api,
      new AssetLocation(basePath)
        .WithPathPrefixOnce("shapes/")
        .WithPathAppendixOnce(".json")
    );
    ViewBox? box =
      shape == null
        ? null
        : Fit(Drawn(shape, keep), Pose.Turn(rotation), shapeFrame);
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

  private static bool[] FacesOf(ShapeElement el) =>
    [
      .. Enumerable
        .Range(0, 6)
        .Select(i =>
          el.FacesResolved?.ElementAtOrDefault(i)
            is { Texture: not null and not "null" }
        ),
    ];

  private static List<Element> Drawn(Shape shape, string[]? keep) {
    var drawn = new List<Element>();
    Walk(
      shape.Elements,
      Pose.Identity,
      keep is { Length: > 0 } ? keep : null,
      drawn
    );
    return drawn;
  }

  // The game's per-segment selection: an exactly named element draws without its children,
  // "name/*" or a trailing '*' draws the subtree, "name/rest" passes "rest" to the children.
  private static bool SelectiveMatch(
    string needle,
    string[] haystack,
    out string[] children
  ) {
    children = [];
    foreach (string text in haystack) {
      if (text.Length == 0)
        continue;
      if (text == needle)
        return true;
      if (
        text == "*"
        || text == needle + "/*"
        || (
          text[^1] == '*'
          && needle.StartsWith(text[..^1], StringComparison.Ordinal)
        )
      ) {
        children = ["*"];
        return true;
      }
      if (
        text.IndexOf('/') != needle.Length
        || !text.StartsWith(needle, StringComparison.Ordinal)
      )
        continue;
      children =
      [
        .. haystack
          .Where(h =>
            h.IndexOf('/') == needle.Length
            && h.StartsWith(needle, StringComparison.Ordinal)
          )
          .Select(h => h[(needle.Length + 1)..]),
      ];
      return true;
    }
    return false;
  }

  private static void Walk(
    ShapeElement[]? elements,
    Pose parent,
    string[]? keep,
    List<Element> drawn
  ) {
    foreach (ShapeElement el in elements ?? []) {
      string[]? childKeep = null;
      if (keep != null && !SelectiveMatch(el.Name ?? "", keep, out childKeep))
        continue;
      Pose pose = parent.Then(Local(el));
      if (IsDrawn(el))
        drawn.Add(new Element(Corners(el, pose), pose.R, FacesOf(el)));
      Walk(
        el.Children,
        pose.Then(new Pose(Identity, Vec(el.From))),
        childKeep,
        drawn
      );
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
  // volume; a tie keeps the earlier frame, so the identity wins a tie. With shapeFrame only the identity.
  private static ViewBox? Fit(List<Element> drawn, Pose turn, bool shapeFrame) {
    if (drawn.Count == 0)
      return null;
    var frames = new List<double[,]> { Identity };
    var points = new List<Vec3d>();
    var turned = new List<Vec3d[]>();
    foreach (Element e in drawn) {
      Vec3d[] corners = e.Corners.Select(turn.Apply).ToArray();
      turned.Add(corners);
      points.AddRange(corners);
      double[,] frame = Mul(turn.R, e.Frame);
      if (!shapeFrame && frames.All(g => !SameFrame(g, frame)))
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
          ],
          turned,
          drawn.Select(e => e.Faces).ToList()
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
  internal static double[,] Rotation(Vec3d degrees) =>
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

  internal static double[,] Mul(double[,] a, double[,] b) {
    var r = new double[3, 3];
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        for (int k = 0; k < 3; k++)
          r[i, j] += a[i, k] * b[k, j];
    return r;
  }

  internal static Vec3d Mul(double[,] m, Vec3d v) =>
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

  private readonly record struct Element(
    Vec3d[] Corners,
    double[,] Frame,
    bool[] Faces
  );
}
