using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the stretch between a looping clip's last keyframe and its first, over one shipped
/// tree's <c>shapes/</c>. That stretch is an ordinary interpolation segment, not a seam the
/// animator skips over.
/// </summary>
public static class LoopingAnimations {
  /// <summary>Threshold (degrees) past which the wrap is treated as a real unwind, not the last
  /// slice of the turn.</summary>
  private const double UnwindDegrees = 180.0;

  private static readonly string[] Axes = ["X", "Y", "Z"];

  /// <summary>Every looping clip in every shape under <paramref name="assetTree"/>'s
  /// <c>shapes/</c> that unwinds across the wrap without <c>rotShortestDistance</c>, or poses an
  /// element at only one end of the clip.</summary>
  /// <returns>Empty when clean.</returns>
  public static IReadOnlyList<string> Check(string assetTree) {
    var offenders = new List<string>();
    foreach (string relative in ShapeFiles(assetTree)) {
      foreach (Clip clip in LoopingClips(relative)) {
        foreach (
          (
            string element,
            JsonElement first,
            JsonElement last
          ) in clip.FirstAndLastPerElement()
        ) {
          foreach (string axis in Axes) {
            double from = Rotation(first, axis);
            double to = Rotation(last, axis);
            if (Math.Abs(to - from) < UnwindDegrees)
              continue;
            if (Flagged(last, axis))
              continue;
            offenders.Add(
              string.Format(
                CultureInfo.InvariantCulture,
                "{0}: clip '{1}' element '{2}' rotation{3} wraps {4:0.##} -> {5:0.##}"
                  + " ({6:0.##} deg) with no rotShortestDistance{3}",
                relative,
                clip.Code,
                element,
                axis,
                to,
                from,
                Math.Abs(to - from)
              )
            );
          }
        }

        // An element keyframed at only one end of the clip hitches or drifts across the wrap.
        int firstFrame = clip.Frames.First();
        int lastFrame = clip.Frames.Last();
        foreach (string element in clip.Elements()) {
          bool atFirst = clip.Poses(element).Any(p => p.frame == firstFrame);
          bool atLast = clip.Poses(element).Any(p => p.frame == lastFrame);
          if (atFirst && atLast)
            continue;
          offenders.Add(
            $"{relative}: clip '{clip.Code}' element '{element}' keyframed at "
              + (
                atFirst
                  ? $"{firstFrame} but not {lastFrame}"
                  : $"{lastFrame} but not {firstFrame}"
              )
          );
        }
      }
    }
    return offenders;
  }

  /// <summary>The shape files under <paramref name="assetTree"/>'s <c>shapes/</c>.</summary>
  public static IReadOnlyList<string> ShapeFiles(string assetTree) {
    if (!Directory.Exists(assetTree))
      return [];
    string root = RepoPaths.Root;
    var files = new List<string>();
    foreach (
      string file in Directory.EnumerateFiles(
        assetTree,
        "*.json",
        SearchOption.AllDirectories
      )
    ) {
      string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
      if (rel.Contains("/shapes/", StringComparison.Ordinal))
        files.Add(rel);
    }
    return files;
  }

  #region Shape reading

  /// <summary>Only clips that say <c>Repeat</c> outright; a clip that stops or eases out never
  /// renders the wrap segment.</summary>
  private static IEnumerable<Clip> LoopingClips(string repoRelativePath) {
    using var doc = JsonDocument.Parse(
      File.ReadAllText(Path.Combine(RepoPaths.Root, repoRelativePath)),
      new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );

    if (
      !doc.RootElement.TryGetProperty("animations", out JsonElement anims)
      || anims.ValueKind != JsonValueKind.Array
    )
      yield break;

    foreach (JsonElement anim in anims.EnumerateArray()) {
      if (
        !anim.TryGetProperty("onAnimationEnd", out JsonElement end)
        || end.GetString() != "Repeat"
      )
        continue;
      if (
        !anim.TryGetProperty("keyframes", out JsonElement keys)
        || keys.ValueKind != JsonValueKind.Array
      )
        continue;

      var frames = keys.EnumerateArray().ToList();
      // A single keyframe is a held pose - there is nothing to interpolate and no wrap to get wrong.
      if (frames.Count < 2)
        continue;

      yield return new Clip(
        anim.TryGetProperty("code", out JsonElement code)
          ? code.GetString() ?? "?"
          : "?",
        frames
      );
    }
  }

  private sealed record Clip(string Code, List<JsonElement> Keyframes) {
    public IEnumerable<int> Frames =>
      Keyframes.Select(k => k.GetProperty("frame").GetInt32());

    public IEnumerable<string> Elements() =>
      Keyframes
        .SelectMany(k =>
          k.TryGetProperty("elements", out JsonElement els)
          && els.ValueKind == JsonValueKind.Object
            ? els.EnumerateObject().Select(p => p.Name)
            : []
        )
        .Distinct();

    public IEnumerable<(int frame, JsonElement pose)> Poses(string element) {
      foreach (JsonElement key in Keyframes) {
        if (
          key.TryGetProperty("elements", out JsonElement els)
          && els.ValueKind == JsonValueKind.Object
          && els.TryGetProperty(element, out JsonElement pose)
        )
          yield return (key.GetProperty("frame").GetInt32(), pose);
      }
    }

    /// <summary>Each element's own first and last poses; the wrap runs between the frames it is
    /// posed at, not the clip's outermost frames.</summary>
    public IEnumerable<(
      string element,
      JsonElement first,
      JsonElement last
    )> FirstAndLastPerElement() {
      foreach (string element in Elements()) {
        var poses = Poses(element).ToList();
        if (poses.Count > 0)
          yield return (element, poses[0].pose, poses[^1].pose);
      }
    }
  }

  private static double Rotation(JsonElement pose, string axis) =>
    pose.TryGetProperty("rotation" + axis, out JsonElement v)
    && v.ValueKind == JsonValueKind.Number
      ? v.GetDouble()
      : 0.0;

  private static bool Flagged(JsonElement pose, string axis) =>
    pose.TryGetProperty("rotShortestDistance" + axis, out JsonElement flag)
    && flag.ValueKind == JsonValueKind.True;

  #endregion
}
