using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ExpandedLib.Testing;

/// <summary>Parses every JSON asset under one shipped tree, and checks that every patch entry
/// declares the side it runs on.</summary>
public static class ShippedJson {
  // blocktypes, itemtypes and recipes are the EnumAppSide.Server asset categories.
  private static readonly string[] ServerOnlyCategories =
  [
    "blocktypes",
    "itemtypes",
    "recipes",
  ];

  /// <summary>Every JSON asset under <paramref name="assetTree"/>: parses, carries no control
  /// character outside tab/LF/CR, and, for a file under <c>patches/</c>, every entry declares its
  /// side.</summary>
  public static IReadOnlyList<string> Check(string assetTree) {
    var offenders = new List<string>();
    foreach (string relative in AssetFiles(assetTree)) {
      byte[] bytes = File.ReadAllBytes(FullPath(relative));

      var badOffsets = new List<string>();
      for (int i = 0; i < bytes.Length; i++) {
        byte b = bytes[i];
        if (b < 0x20 && b != 0x09 && b != 0x0a && b != 0x0d)
          badOffsets.Add($"0x{b:x2} at offset {i}");
      }
      if (badOffsets.Count > 0)
        offenders.Add(
          $"{relative} contains {badOffsets.Count} control character(s): "
            + string.Join(", ", badOffsets.Take(8))
        );

      JsonDocument doc;
      try {
        doc = Parse(bytes);
      } catch (Exception ex) {
        offenders.Add($"{relative} is not valid JSON: {ex.Message}");
        continue;
      }
      using (doc) {
        if (!IsPatchFile(relative))
          continue;
        int index = 0;
        foreach (JsonElement entry in doc.RootElement.EnumerateArray()) {
          if (
            !entry.TryGetProperty("side", out JsonElement side)
            || side.ValueKind != JsonValueKind.String
          )
            offenders.Add($"{relative} [{index}] declares no side");
          else if (ServerOnlyCategory(entry) && side.GetString() != "Server")
            offenders.Add(
              $"{relative} [{index}] targets a server-only category but declares "
                + $"\"{side.GetString()}\""
            );
          index++;
        }
      }
    }
    return offenders;
  }

  /// <summary>The patch files under <paramref name="assetTree"/>.</summary>
  public static IReadOnlyList<string> PatchFiles(string assetTree) =>
    [.. AssetFiles(assetTree).Where(IsPatchFile)];

  private static bool IsPatchFile(string relativePath) =>
    relativePath.Contains("/patches/", StringComparison.Ordinal);

  private static bool ServerOnlyCategory(JsonElement entry) =>
    entry.TryGetProperty("file", out JsonElement file)
    && file.GetString() is { } target
    && ServerOnlyCategories.Contains(
      target.Split(':').Last().Split('/').First()
    );

  private static JsonDocument Parse(byte[] utf8Json) =>
    JsonDocument.Parse(
      utf8Json,
      new JsonDocumentOptions {
        // The game's loader tolerates both.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );

  private static string FullPath(string repoRelativePath) =>
    Path.Combine(
      RepoPaths.Root,
      repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
    );

  // Every JSON under the tree, repo-relative and forward-slashed.
  private static IEnumerable<string> AssetFiles(string assetTree) {
    if (!Directory.Exists(assetTree))
      yield break;
    string root = RepoPaths.Root;
    foreach (
      string file in Directory.EnumerateFiles(
        assetTree,
        "*.json",
        SearchOption.AllDirectories
      )
    )
      yield return Path.GetRelativePath(root, file).Replace('\\', '/');
  }
}
