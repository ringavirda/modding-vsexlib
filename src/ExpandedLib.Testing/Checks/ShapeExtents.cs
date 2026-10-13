using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Measures a shape file: the bounding box of everything it draws, in voxels.</summary>
public static class ShapeExtents {
  /// <summary>
  /// The composed extents of every element in <paramref name="path"/>, as (width, thickness, length) on
  /// x / y / z.
  /// </summary>
  /// <param name="element">One element to measure, children included. Null measures the whole file.</param>
  [CheckHelper("measures the extents of a shape file")]
  public static (float Width, float Thickness, float Length) Of(
    string path,
    string? element = null
  ) {
    (float[] min, float[] max) = Bounds(path, element);
    return (max[0] - min[0], max[1] - min[1], max[2] - min[2]);
  }

  /// <summary>
  /// The composed bounding box of <paramref name="element"/> (or of the whole file), as minimum and
  /// maximum voxel corners on x / y / z. Rotation is composed down the tree; scale is left at 1.
  /// </summary>
  [CheckHelper("measures the bounding box of a shape or element")]
  public static (float[] Min, float[] Max) Bounds(
    string path,
    string? element = null
  ) {
    var corners = new List<float[]>();
    JObject shape = JObject.Parse(File.ReadAllText(path));
    JToken? root = shape["elements"];

    if (element != null) {
      JToken? found = Find(root, element);
      if (found == null)
        throw new KeyNotFoundException($"{path} has no element '{element}'");
      // Measured in its own frame: a stage's absolute placement in the file is layout, not geometry.
      Walk(found, Mat4f.Create(), corners);
    } else {
      foreach (JToken el in root ?? new JArray())
        Walk(el, Mat4f.Create(), corners);
    }

    return (
      [.. Enumerable.Range(0, 3).Select(i => corners.Min(c => c[i]))],
      [.. Enumerable.Range(0, 3).Select(i => corners.Max(c => c[i]))]
    );
  }

  /// <summary>One element of a shape with the surface of its box sampled.</summary>
  /// <param name="Path">The element's names from the top of the tree, joined with <c>/</c>.</param>
  /// <param name="Leaf">Whether the element has no children.</param>
  /// <param name="Middle">The centre of the element's box, in voxels.</param>
  /// <param name="Points">Points on the six faces of the element's box, in voxels, at most the
  /// sampling step apart, rotations composed down the tree.</param>
  internal sealed record DrawnElement(
    string Path,
    bool Leaf,
    float[] Middle,
    List<float[]> Points
  );

  /// <summary>Every element of <paramref name="path"/> with its box surface sampled on a
  /// <paramref name="step"/>-voxel grid, parents before their children.</summary>
  internal static List<DrawnElement> Surfaces(string path, float step) {
    var found = new List<DrawnElement>();
    JObject shape = JObject.Parse(File.ReadAllText(path));
    foreach (JToken el in shape["elements"] ?? new JArray())
      Sample(el, Mat4f.Create(), "", step, found);
    return found;
  }

  private static void Sample(
    JToken element,
    float[] parent,
    string parentPath,
    float step,
    List<DrawnElement> found
  ) {
    float[] from = Point(element["from"]);
    float[] to = Point(element["to"]);
    float[] transform = Mat4f.Mul(Mat4f.Create(), parent, Local(element, from));
    string name = (string?)element["name"] ?? "";
    string full = parentPath.Length == 0 ? name : parentPath + "/" + name;
    float[] size = [to[0] - from[0], to[1] - from[1], to[2] - from[2]];
    JToken? children = element["children"];

    var points = new List<float[]>();
    float[][] axes =
    [
      .. size.Select(s =>
        Enumerable
          .Range(0, Math.Max(2, (int)Math.Ceiling(s / step) + 1))
          .Select(i => i * s / Math.Max(1, (int)Math.Ceiling(s / step)))
          .ToArray()
      ),
    ];
    for (int fixedAxis = 0; fixedAxis < 3; fixedAxis++) {
      int a = (fixedAxis + 1) % 3,
        b = (fixedAxis + 2) % 3;
      foreach (float face in new[] { 0f, size[fixedAxis] })
        foreach (float u in axes[a])
          foreach (float v in axes[b]) {
            var local = new float[3];
            local[fixedAxis] = face;
            local[a] = u;
            local[b] = v;
            points.Add(
              Mat4f.MulWithVec4(transform, local[0], local[1], local[2], 1f)[..3]
            );
          }
    }

    found.Add(
      new DrawnElement(
        full,
        children == null || !children.Any(),
        Mat4f.MulWithVec4(transform, size[0] / 2, size[1] / 2, size[2] / 2, 1f)[
          ..3
        ],
        points
      )
    );
    foreach (JToken child in children ?? new JArray())
      Sample(child, transform, full, step, found);
  }

  private static JToken? Find(JToken? elements, string name) {
    foreach (JToken el in elements ?? new JArray()) {
      if ((string?)el["name"] == name)
        return el;
      if (Find(el["children"], name) is { } nested)
        return nested;
    }
    return null;
  }

  private static void Walk(
    JToken element,
    float[] parent,
    List<float[]> corners
  ) {
    float[] from = Point(element["from"]);
    float[] to = Point(element["to"]);
    float[] transform = Mat4f.Mul(Mat4f.Create(), parent, Local(element, from));

    // The element's box is drawn from its own origin, which the local transform has already placed.
    foreach (float x in new[] { 0f, to[0] - from[0] })
      foreach (float y in new[] { 0f, to[1] - from[1] })
        foreach (float z in new[] { 0f, to[2] - from[2] })
          corners.Add(Mat4f.MulWithVec4(transform, x, y, z, 1f));

    foreach (JToken child in element["children"] ?? new JArray())
      Walk(child, transform, corners);
  }

  /// <summary>One element's own placement within its parent: rotate about <c>rotationOrigin</c> in
  /// X, Y, Z order, then translate to <c>from</c>.</summary>
  private static float[] Local(JToken element, float[] from) {
    float[] origin = Point(element["rotationOrigin"]);
    float[] m = Mat4f.Create();
    Mat4f.Translate(m, m, origin[0], origin[1], origin[2]);
    Mat4f.RotateX(m, m, Degrees(element["rotationX"]) * GameMath.DEG2RAD);
    Mat4f.RotateY(m, m, Degrees(element["rotationY"]) * GameMath.DEG2RAD);
    Mat4f.RotateZ(m, m, Degrees(element["rotationZ"]) * GameMath.DEG2RAD);
    Mat4f.Translate(
      m,
      m,
      from[0] - origin[0],
      from[1] - origin[1],
      from[2] - origin[2]
    );
    return m;
  }

  private static float[] Point(JToken? point) =>
    point == null
      ? [0f, 0f, 0f]
      : [.. Enumerable.Range(0, 3).Select(i => (float)point[i]!)];

  private static float Degrees(JToken? node) => node == null ? 0f : (float)node;
}
