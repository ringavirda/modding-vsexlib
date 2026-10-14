#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>The two files <see cref="CraftWalk.Run"/> reads: the kit of vanilla-made codes a mod's
/// player brings in, and the walk's known failures.</summary>
internal static class CraftWalkFiles
{
  /// <summary>The kit rows of <paramref name="path"/>: code to the vanilla route that makes it.</summary>
  internal static IReadOnlyDictionary<string, string> Kit(string? path) =>
    Rows(path, "route", "kit");

  /// <summary>The known rows of <paramref name="path"/>: failure key to the reason it stands.</summary>
  internal static IReadOnlyDictionary<string, string> Known(string? path) =>
    Rows(path, "why", "known");

  private static Dictionary<string, string> Rows(
    string? path,
    string field,
    string file
  )
  {
    if (path == null || !File.Exists(path))
      return [];
    if (JToken.Parse(File.ReadAllText(path)) is not JObject root)
      throw new InvalidOperationException(
        $"{path}: the {file} file is not an object."
      );
    return root.Properties()
      .ToDictionary(
        p => p.Name,
        p =>
          (p.Value is JObject row ? (string?)row[field] : (string?)p.Value)
            is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException(
              $"{path}: row \"{p.Name}\" has no {field}."
            )
      );
  }
}
#endif
