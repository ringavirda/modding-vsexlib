using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// The handbook authoring pipeline: <c>docs/handbook/NN-*.html</c> is the hand-edited source for a
/// page's body, shipped as a lang-key string in <c>en.json</c>. Only the body is synced; a page's
/// <c>title</c> key stays hand-authored.
/// </summary>
public static class HandbookSync {
  /// <summary>One handbook page: its authoring HTML, shipped descriptor, and lang key
  /// (<c>domain:</c> prefix stripped).</summary>
  public sealed record Page(
    string Domain,
    string Number,
    string HtmlPath,
    string DescriptorPath,
    string LangKey
  ) {
    /// <summary>A stable, serializable id that doubles as the xUnit theory case name.</summary>
    public override string ToString() => $"{Domain}/{Number} [{LangKey}]";
  }

  private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
  private static readonly Regex LeadingNumber = new(
    @"^\d+",
    RegexOptions.Compiled
  );

  /// <summary>True when <c>EXLIB_WRITE_HANDBOOK=1</c>.</summary>
  public static bool WriteRequested =>
    Environment.GetEnvironmentVariable("EXLIB_WRITE_HANDBOOK") == "1";

  #region The transform

  /// <summary>The authoring HTML as it must appear in the lang file: unescaped and
  /// whitespace-collapsed.</summary>
  public static string Normalize(string html) =>
    Whitespace.Replace(html.Replace("\\\"", "\""), " ").Trim();

  #endregion

  #region Discovery

  /// <summary>Every mod asset domain that ships handbook pages, in order.</summary>
  public static IReadOnlyList<string> Domains() =>
    RepoPaths
      .AllAssetTrees()
      .Where(d => Directory.Exists(Path.Combine(d, "config", "handbook")))
      .Select(d => new DirectoryInfo(d).Name)
      .OrderBy(d => d, StringComparer.Ordinal)
      .ToList();

  /// <summary>The pages of <paramref name="domain"/> that have both an authoring source and a
  /// shipped descriptor, joined on the <c>NN-</c> ordering prefix.</summary>
  public static IReadOnlyList<Page> Pages(string domain) {
    var sources = SourcesByNumber(domain);
    var pages = new List<Page>();

    foreach ((string number, string descriptor) in DescriptorsByNumber(domain)) {
      if (!sources.TryGetValue(number, out string? html))
        continue;
      string? key = LangKeyOf(descriptor);
      if (key != null)
        pages.Add(new Page(domain, number, html, descriptor, key));
    }
    return pages;
  }

  /// <summary>Everything wrong with <paramref name="domain"/>'s handbook wiring that is not a
  /// prose mismatch.</summary>
  /// <returns>Empty when the two trees line up.</returns>
  public static IReadOnlyList<string> Problems(string domain) {
    var sources = SourcesByNumber(domain);
    var descriptors = DescriptorsByNumber(domain);
    JObject lang = Lang(domain);
    var problems = new List<string>();

    foreach ((string number, string descriptor) in descriptors) {
      string name = Path.GetFileName(descriptor);
      if (!sources.ContainsKey(number))
        problems.Add(
          $"{domain}: shipped page {name} has no authoring source mods/{domain}/docs/handbook/{number}-*.html"
        );

      string? key = LangKeyOf(descriptor);
      if (key == null)
        problems.Add(
          $"{domain}: page descriptor {name} declares no 'text' lang key"
        );
      else if (lang[key] == null)
        problems.Add(
          $"{domain}: page {name} points at '{key}', which mods/{domain}/assets/{domain}/lang/en.json does not define"
        );
    }

    foreach ((string number, string html) in sources)
      if (!descriptors.ContainsKey(number))
        problems.Add(
          $"{domain}: authoring source {Path.GetFileName(html)} ships nowhere - no mods/{domain}/assets/{domain}/config/handbook/{number}-*.json"
        );

    return problems;
  }

  #endregion

  #region Check + write

  /// <summary>Checks one page's shipped text against its authoring source.</summary>
  /// <returns><c>(true, "")</c> on match, else a message naming the page and the first differing
  /// character.</returns>
  public static (bool ok, string message) Check(Page page) {
    string want = Normalize(File.ReadAllText(page.HtmlPath));
    string? have = (string?)Lang(page.Domain)[page.LangKey];

    if (have == want)
      return (true, "");

    string relative =
      $"mods/{page.Domain}/docs/handbook/{Path.GetFileName(page.HtmlPath)}";
    if (have == null)
      return (
        false,
        $"{page}: lang key is missing entirely; {relative} has {want.Length} chars of source"
      );

    return (
      false,
      $"{page}: shipped text ({have.Length} chars) differs from {relative} ({want.Length} chars)\n"
        + $"  first difference at char {FirstDifference(have, want)}:\n"
        + $"    shipped: …{Excerpt(have, FirstDifference(have, want))}…\n"
        + $"    source : …{Excerpt(want, FirstDifference(have, want))}…\n"
        + "  Re-run with EXLIB_WRITE_HANDBOOK=1 to adopt the authoring source."
    );
  }

  /// <summary>Re-blesses <paramref name="domain"/>'s lang file from its authoring sources.
  /// Opt-in: call only when <see cref="WriteRequested"/>.</summary>
  /// <returns>The lang keys that changed.</returns>
  public static IReadOnlyList<string> WriteAll(string domain) {
    string path = LangPath(domain);
    if (!File.Exists(path))
      return [];

    string original = File.ReadAllText(path);
    JObject lang = JObject.Parse(original);
    var changed = new List<string>();

    foreach (Page page in Pages(domain)) {
      string want = Normalize(File.ReadAllText(page.HtmlPath));
      if ((string?)lang[page.LangKey] == want)
        continue;
      lang[page.LangKey] = want;
      changed.Add(page.LangKey);
    }

    if (changed.Count > 0)
      File.WriteAllText(
        path,
        Serialize(lang)
          + (original.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "")
      );
    return changed;
  }

  // Converter array passed explicitly; the game's runtime Newtonsoft lacks the convenience
  // overloads present at compile time.
  private static string Serialize(JObject lang) =>
    lang.ToString(Formatting.Indented, Array.Empty<JsonConverter>());

  /// <summary>Rewrites <paramref name="domain"/>'s authoring sources from the shipped lang
  /// values, the reverse of <see cref="WriteAll"/>.</summary>
  /// <returns>The authoring files it changed.</returns>
  /// <exception cref="InvalidOperationException">The exported HTML does not
  /// <see cref="Normalize"/> back to the shipped value.</exception>
  public static IReadOnlyList<string> ExportAll(string domain) {
    JObject lang = Lang(domain);
    var changed = new List<string>();

    foreach (Page page in Pages(domain)) {
      var value = (string?)lang[page.LangKey];
      if (value == null)
        continue;

      string html = ToAuthoringHtml(value);
      if (Normalize(html) != value)
        throw new InvalidOperationException(
          $"{page}: export would change the shipped text - refusing to write {page.HtmlPath}"
        );

      if (
        File.Exists(page.HtmlPath)
        && Normalize(File.ReadAllText(page.HtmlPath)) == value
      )
        continue;

      File.WriteAllText(page.HtmlPath, html);
      changed.Add(page.HtmlPath);
    }
    return changed;
  }

  /// <summary>Column the exporter wraps authoring lines at.</summary>
  private const int WrapColumn = 78;

  // A lang value as an authoring file: escaped quotes and greedy word wrap, breaking only at
  // existing spaces.
  private static string ToAuthoringHtml(string value) {
    var lines = new List<string>();
    var line = new System.Text.StringBuilder();

    foreach (string word in value.Replace("\"", "\\\"").Split(' ')) {
      if (line.Length > 0 && line.Length + 1 + word.Length > WrapColumn) {
        lines.Add(line.ToString());
        line.Clear();
      }
      if (line.Length > 0)
        line.Append(' ');
      line.Append(word);
    }
    if (line.Length > 0)
      lines.Add(line.ToString());

    return string.Join("\n", lines) + "\n";
  }

  #endregion

  #region Paths

  private static string LangPath(string domain) =>
    Path.Combine(RepoPaths.Assets(domain), "lang", "en.json");

  private static JObject Lang(string domain) {
    string path = LangPath(domain);
    return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : [];
  }

  private static SortedDictionary<string, string> SourcesByNumber(
    string domain
  ) => ByNumber(Path.Combine(RepoPaths.Docs(domain), "handbook"), "*.html");

  private static SortedDictionary<string, string> DescriptorsByNumber(
    string domain
  ) =>
    ByNumber(
      Path.Combine(RepoPaths.Assets(domain), "config", "handbook"),
      "*.json"
    );

  // Files in `dir` keyed by their leading ordering number; a file without one is skipped.
  private static SortedDictionary<string, string> ByNumber(
    string dir,
    string pattern
  ) {
    var byNumber = new SortedDictionary<string, string>(StringComparer.Ordinal);
    if (!Directory.Exists(dir))
      return byNumber;

    foreach (string file in Directory.EnumerateFiles(dir, pattern)) {
      Match m = LeadingNumber.Match(Path.GetFileNameWithoutExtension(file));
      if (m.Success)
        byNumber[m.Value] = file;
    }
    return byNumber;
  }

  // The lang key a page descriptor's "text" field names, with its domain prefix stripped.
  private static string? LangKeyOf(string descriptorPath) {
    var text = (string?)JObject.Parse(File.ReadAllText(descriptorPath))["text"];
    if (string.IsNullOrEmpty(text))
      return null;
    int colon = text!.IndexOf(':');
    return colon >= 0 ? text[(colon + 1)..] : text;
  }

  #endregion

  #region Diff reporting

  private static int FirstDifference(string a, string b) {
    int i = 0;
    while (i < a.Length && i < b.Length && a[i] == b[i])
      i++;
    return i;
  }

  private static string Excerpt(string s, int at, int radius = 60) {
    int start = Math.Max(0, at - radius / 2);
    return s.Substring(start, Math.Min(radius, s.Length - start));
  }

  #endregion
}
