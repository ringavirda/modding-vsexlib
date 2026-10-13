#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>The clip laws' exceptions file: clips nothing drives, and findings known and awaiting
/// the task named in their reason.</summary>
/// <param name="Static">Clip key (<c>&lt;shape base&gt; &lt;clip&gt;</c>) to the reason it needs no driver.</param>
/// <param name="Known">Law name to finding key to the reason it is accepted.</param>
internal sealed record ClipExceptions(
  IReadOnlyDictionary<string, string> Static,
  IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Known
)
{
  internal static ClipExceptions Read(string? path)
  {
    if (path == null || !System.IO.File.Exists(path))
      return new ClipExceptions(
        new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyDictionary<string, string>>()
      );
    JObject root = JObject.Parse(System.IO.File.ReadAllText(path));
    return new ClipExceptions(
      Reasons(root["static"]),
      (root["known"] as JObject)
        ?.Properties()
        .ToDictionary(
          p => p.Name,
          p => (IReadOnlyDictionary<string, string>)Reasons(p.Value)
        )
        ?? new Dictionary<string, IReadOnlyDictionary<string, string>>()
    );
  }

  /// <summary>The findings of <paramref name="law"/> less those its known rows accept, then one
  /// finding per known row that accepted none.</summary>
  /// <param name="law">The law's name.</param>
  /// <param name="found">Each finding's key and its line.</param>
  internal IReadOnlyList<string> Apply(
    string law,
    IReadOnlyList<(string Key, string Line)> found
  )
  {
    IReadOnlyDictionary<string, string> known =
      Known.GetValueOrDefault(law) ?? new Dictionary<string, string>();
    HashSet<string> keys = [.. found.Select(f => f.Key)];
    return
    [
      .. found.Where(f => !known.ContainsKey(f.Key)).Select(f => f.Line),
      .. known
        .Keys.Where(k => !keys.Contains(k))
        .Select(k => $"{k} | known row matches no finding; remove it"),
    ];
  }

  private static Dictionary<string, string> Reasons(JToken? token) =>
    (token as JObject)
      ?.Properties()
      .ToDictionary(p => p.Name, p => (string)p.Value!)
    ?? new Dictionary<string, string>();
}
#endif
