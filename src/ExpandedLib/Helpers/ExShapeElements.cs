using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>Prunes a loaded <see cref="Shape"/> to a chosen set of element paths, the mesh-side counterpart of a blocktype's <c>selectiveElements</c>.</summary>
public static class ExShapeElements {
  /// <summary>Whether the element at <paramref name="path"/> should be kept, given <paramref name="patterns"/>.</summary>
  public static bool Matches(string path, IReadOnlyCollection<string> patterns) {
    foreach (string raw in patterns) {
      // A trailing "/*" or a lone "*" both reduce to a prefix.
      string p =
        raw == "*" ? ""
        : raw.EndsWith("/*") ? raw[..^2]
        : raw;
      if (p.Length == 0)
        return true;
      if (IsSegmentPrefix(path, p) || IsSegmentPrefix(p, path))
        return true;
    }
    return false;
  }

  // Prefix on whole segments only: "Items1" must not match "Items10".
  private static bool IsSegmentPrefix(string prefix, string path) =>
    path.Length >= prefix.Length
    && path.StartsWith(prefix, System.StringComparison.Ordinal)
    && (path.Length == prefix.Length || path[prefix.Length] == '/');

  /// <summary>Returns a copy of <paramref name="shape"/> containing only the elements <paramref name="keep"/> selects; the original is not modified.</summary>
  public static Shape Pruned(Shape shape, IReadOnlyCollection<string> keep) {
    Shape copy = shape.Clone();
    copy.Elements = PruneLevel(copy.Elements, "", keep);
    return copy;
  }

  private static ShapeElement[] PruneLevel(
    ShapeElement[]? elements,
    string parentPath,
    IReadOnlyCollection<string> keep
  ) {
    if (elements == null)
      return [];

    var kept = new List<ShapeElement>();
    foreach (ShapeElement el in elements) {
      string path =
        parentPath.Length == 0 ? el.Name ?? "" : parentPath + "/" + el.Name;
      if (!Matches(path, keep))
        continue;
      el.Children = PruneLevel(el.Children, path, keep);
      kept.Add(el);
    }
    return [.. kept];
  }

  /// <summary>Returns a copy of <paramref name="shape"/> with every face pointing at texture key <paramref name="from"/> repointed at <paramref name="to"/>; <paramref name="to"/> must be a key the blocktype declares.</summary>
  public static Shape Retextured(Shape shape, string from, string to) {
    Shape copy = shape.Clone();
    RetextureLevel(copy.Elements, "#" + from, "#" + to);
    return copy;
  }

  private static void RetextureLevel(
    ShapeElement[]? elements,
    string from,
    string to
  ) {
    foreach (ShapeElement el in elements ?? []) {
      ShapeElementFace[] faces = el.FacesResolved ?? [];
      for (int i = 0; i < faces.Length; i++)
        if (faces[i]?.Texture == from)
          faces[i] = Repointed(faces[i], to);
      RetextureLevel(el.Children, from, to);
    }
  }

  // ShapeElement.Clone shares FacesResolved's face objects with the source; the array slot is ours
  // to overwrite, the face behind it is not.
  private static ShapeElementFace Repointed(ShapeElementFace face, string to) =>
    new() {
      Texture = to,
      Uv = (float[]?)face.Uv?.Clone(),
      Rotation = face.Rotation,
      Glow = face.Glow,
      Enabled = face.Enabled,
      ReflectiveMode = face.ReflectiveMode,
      WindMode = (sbyte[]?)face.WindMode?.Clone(),
      WindData = (sbyte[]?)face.WindData?.Clone(),
    };

  /// <summary>Every element path in <paramref name="shape"/>, parent-first.</summary>
  public static IEnumerable<string> AllPaths(Shape shape) =>
    Walk(shape.Elements, "");

  private static IEnumerable<string> Walk(
    ShapeElement[]? elements,
    string parentPath
  ) {
    foreach (ShapeElement el in elements ?? []) {
      string path =
        parentPath.Length == 0 ? el.Name ?? "" : parentPath + "/" + el.Name;
      yield return path;
      foreach (string child in Walk(el.Children, path))
        yield return child;
    }
  }
}
