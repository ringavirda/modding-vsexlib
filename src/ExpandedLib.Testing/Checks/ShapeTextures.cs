using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// No shipped shape points a texture at a file on the author's machine. The game reads every
/// <c>textures</c> value as an asset location, so a Model Creator path draws the missing-texture
/// placeholder for every player whose disk does not hold that file.
/// </summary>
public static class ShapeTextures {
  private static readonly Regex EditorPath = new(
    @"^[/\\]|^[A-Za-z]:[/\\]|wsl\.localhost",
    RegexOptions.IgnoreCase
  );

  /// <summary>Whether <paramref name="value"/> is a path on the author's disk rather than an asset
  /// location: it starts with a slash or a drive letter, or names <c>wsl.localhost</c>.</summary>
  /// <param name="value">A <c>textures</c> value from a shape.</param>
  /// <returns>True for an editor path; false for an asset location, domained or bare.</returns>
  public static bool IsEditorPath(string value) => EditorPath.IsMatch(value);

  /// <summary>Every string value of a top-level <c>textures</c> map, in every <c>*.json</c> shape
  /// under <paramref name="shapesDirectory"/>, that <see cref="IsEditorPath"/> names.</summary>
  /// <param name="shapesDirectory">A mod's <c>assets/{domain}/shapes</c> folder, searched
  /// recursively.</param>
  /// <returns>One line per value, <c>relative/path.json: key = value</c> with the path relative to
  /// <paramref name="shapesDirectory"/>, in file order; empty when clean.</returns>
  /// <exception cref="DirectoryNotFoundException"><paramref name="shapesDirectory"/> does not
  /// exist.</exception>
  /// <exception cref="JsonReaderException">A shape is not a JSON object.</exception>
  public static IReadOnlyList<string> EditorPaths(string shapesDirectory) {
    var offenders = new List<string>();
    foreach (
      string file in Directory.GetFiles(
        shapesDirectory,
        "*.json",
        SearchOption.AllDirectories
      )
    ) {
      if (JObject.Parse(File.ReadAllText(file))["textures"] is not JObject map)
        continue;
      offenders.AddRange(
        map.Properties()
          .Where(p => p.Value.Type == JTokenType.String)
          .Where(p => IsEditorPath((string)p.Value!))
          .Select(p =>
            $"{Path.GetRelativePath(shapesDirectory, file)}: {p.Name} = {p.Value}"
          )
      );
    }
    return offenders;
  }
}
