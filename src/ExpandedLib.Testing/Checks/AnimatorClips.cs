using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards how every clip in one shipped tree's <c>shapes/</c> ends: <c>Repeat</c> (the default
/// when absent) or <c>Hold</c>. A clip ending otherwise leaves the animator with nothing active
/// after one play, and a block drawn only through its animator vanishes.
/// </summary>
/// <remarks><c>onActivityStopped</c> is not read.</remarks>
public static class AnimatorClips {
  /// <summary>What one run read and found.</summary>
  /// <param name="Shapes">The shape files read.</param>
  /// <param name="Clips">The clips across those shapes.</param>
  /// <param name="Findings">One line per clip that ends, as
  /// <c>{shape}: clip '{code}' ends {mode}</c>; <see cref="Key"/> keys it.</param>
  public sealed record Result(
    int Shapes,
    int Clips,
    IReadOnlyList<string> Findings
  );

  /// <summary>Every clip under <paramref name="assetTree"/>'s <c>shapes/</c> whose
  /// <c>onAnimationEnd</c> is neither <c>Repeat</c> nor <c>Hold</c>, compared ignoring
  /// case.</summary>
  /// <param name="assetTree">A mod's asset tree, <c>assets/{domain}</c>; a missing tree reads
  /// nothing.</param>
  /// <returns>The shapes and clips read, and one finding per clip that ends; no findings when
  /// clean.</returns>
  /// <exception cref="JsonException">A shape file is not valid JSON.</exception>
  /// <exception cref="InvalidOperationException">A clip is not a JSON object, or its
  /// <c>code</c> is neither a string nor null.</exception>
  /// <exception cref="IOException">A shape file cannot be read.</exception>
  /// <exception cref="UnauthorizedAccessException">A shape file may not be read.</exception>
  public static Result Check(string assetTree) {
    IReadOnlyList<string> files = LoopingAnimations.ShapeFiles(assetTree);
    int clips = 0;
    var findings = new List<string>();
    foreach (string relative in files) {
      using var doc = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, relative)),
        new JsonDocumentOptions {
          CommentHandling = JsonCommentHandling.Skip,
          AllowTrailingCommas = true,
        }
      );
      if (
        !doc.RootElement.TryGetProperty("animations", out JsonElement anims)
        || anims.ValueKind != JsonValueKind.Array
      )
        continue;

      foreach (JsonElement anim in anims.EnumerateArray()) {
        clips++;
        string end =
          anim.TryGetProperty("onAnimationEnd", out JsonElement e)
          && e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : "Repeat";
        if (
          end.Equals("Repeat", StringComparison.OrdinalIgnoreCase)
          || end.Equals("Hold", StringComparison.OrdinalIgnoreCase)
        )
          continue;
        string code = anim.TryGetProperty("code", out JsonElement c)
          ? c.GetString() ?? "?"
          : "?";
        findings.Add($"{relative}: clip '{code}' ends {end}");
      }
    }
    return new Result(files.Count, clips, findings);
  }

  /// <summary>The key of one finding: its shape path and clip,
  /// <c>{shape}: clip '{code}'</c>.</summary>
  [CheckHelper("keys a finding by shape and clip for a guard's lists")]
  public static string Key(string finding) =>
    finding[..finding.LastIndexOf(" ends ", StringComparison.Ordinal)];
}
