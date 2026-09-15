using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Localization guard over one shipped lang tree: each translated locale must carry the same key
/// set as English, and every shared value must reference the same placeholder set (<c>{0}</c>,
/// <c>{1:F0}</c>).
/// </summary>
public static class LangParity {
  private static readonly Regex Placeholder = new(
    @"\{(\d+)(?::[^}]*)?\}",
    RegexOptions.Compiled
  );

  /// <summary>Every non-English locale under <paramref name="langTree"/> carries exactly the
  /// English key set with matching placeholders.</summary>
  /// <returns>Empty when clean, or when <paramref name="langTree"/> has no <c>en.json</c>.</returns>
  public static IReadOnlyList<string> Check(string langTree) {
    string enPath = Path.Combine(langTree, "en.json");
    if (!File.Exists(enPath))
      return [];

    string domain = new DirectoryInfo(langTree).Parent?.Name ?? "";
    Dictionary<string, string> en = Load(enPath);
    var offenders = new List<string>();

    foreach (string locPath in LocaleFilePaths(langTree)) {
      string label = $"{domain}/{Path.GetFileName(locPath)}";
      Dictionary<string, string> loc = Load(locPath);

      List<string> missing = [.. en.Keys.Except(loc.Keys).Order()];
      List<string> extra = [.. loc.Keys.Except(en.Keys).Order()];
      if (missing.Count > 0 || extra.Count > 0)
        offenders.Add(
          $"{label}: {missing.Count} missing, {extra.Count} extra vs en.\n"
            + (
              missing.Count > 0
                ? "  missing: " + string.Join(", ", missing.Take(20)) + "\n"
                : ""
            )
            + (
              extra.Count > 0
                ? "  extra: " + string.Join(", ", extra.Take(20))
                : ""
            )
        );

      List<string> drift = [];
      foreach ((string key, string enVal) in en) {
        // Missing keys are the other rule's job; only compare values present in both.
        if (!loc.TryGetValue(key, out string? locVal))
          continue;
        HashSet<int> enIx = Indices(enVal);
        HashSet<int> locIx = Indices(locVal);
        if (!enIx.SetEquals(locIx))
          drift.Add($"{key} (en:[{Join(enIx)}] loc:[{Join(locIx)}])");
      }
      if (drift.Count > 0)
        offenders.Add(
          $"{label}: placeholder drift in {drift.Count} key(s):\n  "
            + string.Join("\n  ", drift.Take(20))
        );
    }

    return offenders;
  }

  /// <summary>The non-English locale files under <paramref name="langTree"/>.</summary>
  public static IReadOnlyList<string> LocaleFiles(string langTree) =>
    [.. LocaleFilePaths(langTree)];

  private static IEnumerable<string> LocaleFilePaths(string langTree) {
    if (!Directory.Exists(langTree))
      yield break;
    foreach (string file in Directory.EnumerateFiles(langTree, "*.json")) {
      if (
        Path.GetFileName(file)
          .Equals("en.json", StringComparison.OrdinalIgnoreCase)
      )
        continue;
      yield return file;
    }
  }

  private static string Join(HashSet<int> ix) => string.Join(",", ix.Order());

  private static HashSet<int> Indices(string value) {
    HashSet<int> set = [];
    foreach (Match m in Placeholder.Matches(value))
      set.Add(int.Parse(m.Groups[1].Value));
    return set;
  }

  private static Dictionary<string, string> Load(string path) {
    var obj = JObject.Parse(File.ReadAllText(path));
    Dictionary<string, string> map = [];
    foreach (JProperty p in obj.Properties())
      map[p.Name] =
        p.Value.Type == JTokenType.String
          ? (string)p.Value!
          : p.Value.ToString();
    return map;
  }
}
