using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExpandedLib.Testing;

/// <summary>
/// The mechanically unambiguous comment style rules over C# sources: typography (em dashes, HTML
/// emphasis, marker glyphs, non-ASCII, filler openers) and size (doc blocks, paragraphs, remarks,
/// summaries). A comment line is one whose first non-blank characters are <c>//</c>.
/// </summary>
public static class CommentStyle {
  private const int MaxDocBlockLines = 16;
  private const int MaxParaPerDocBlock = 3;
  private const int MaxSummaryLines = 5;
  private const int MaxRemarkLines = 2;

  private static readonly Regex CommentLine = new(
    @"^\s*(///|//)",
    RegexOptions.Compiled
  );
  private static readonly Regex XmlDocLine = new(
    @"^\s*///",
    RegexOptions.Compiled
  );

  private static readonly Regex HtmlEmphasis = new(
    @"</?(b|i|em|strong)>",
    RegexOptions.IgnoreCase
  );

  // These read as generated noise and carry no information a sentence cannot.
  private static readonly char[] Markers =
  [
    '\u2605',
    '\u26d4',
    '\u26a0',
    '\u2705',
    '\u24d8',
    '\u2757',
    '\u2b50',
  ];

  private static readonly Regex Filler = new(
    @"(^|\s)(Note that|It is worth noting|Importantly|Crucially|Remember that)\b",
    RegexOptions.IgnoreCase
  );

  /// <summary>A C# source: its path relative to the repository root, and its lines.</summary>
  /// <param name="Relative">The path, '/'-separated; it names the file in every finding.</param>
  /// <param name="Lines">The file's lines, without line endings.</param>
  public sealed record SourceFile(string Relative, string[] Lines);

  /// <summary>Reads every <c>*.cs</c> file under each of <paramref name="folders"/>, recursively,
  /// skipping <c>*.g.cs</c> and anything under a <c>bin</c> or <c>obj</c> folder.</summary>
  /// <param name="root">The repository root; the folders and each file's
  /// <see cref="SourceFile.Relative"/> are relative to it.</param>
  /// <param name="folders">'/'-separated folders under <paramref name="root"/>; a missing one is
  /// skipped.</param>
  /// <returns>The files, folder by folder in the given order; empty when no folder holds one.
  /// </returns>
  /// <exception cref="IOException">A file cannot be read.</exception>
  [CheckHelper("reads the corpus the rules run over")]
  public static IReadOnlyList<SourceFile> Sources(
    string root,
    IEnumerable<string> folders
  ) {
    var files = new List<SourceFile>();
    foreach (string folder in folders) {
      string dir = Path.Combine(root, folder);
      if (!Directory.Exists(dir))
        continue;
      foreach (
        string path in Directory.EnumerateFiles(
          dir,
          "*.cs",
          SearchOption.AllDirectories
        )
      ) {
        // Generated sources are not hand-authored; the style rules do not apply to them.
        if (path.EndsWith(".g.cs", StringComparison.Ordinal))
          continue;
        string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (
          rel.Contains("/bin/", StringComparison.Ordinal)
          || rel.Contains("/obj/", StringComparison.Ordinal)
        )
          continue;
        files.Add(new SourceFile(rel, File.ReadAllLines(path)));
      }
    }
    return files;
  }

  /// <summary>An assertion message: <paramref name="rule"/>, the count of
  /// <paramref name="hits"/>, and the first 25 of them, one per line.</summary>
  /// <param name="rule">The rule's sentence, printed first.</param>
  /// <param name="hits">The findings of one rule.</param>
  /// <returns>The message.</returns>
  [CheckHelper("formats a rule's findings for an assertion message")]
  public static string Report(string rule, IEnumerable<string> hits) {
    var list = hits.ToList();
    return $"{rule}\n  {list.Count} violation(s):\n"
      + string.Join("\n", list.Take(25).Select(h => "    " + h))
      + (list.Count > 25 ? $"\n    ... and {list.Count - 25} more" : "");
  }

  /// <summary>Each comment line of <paramref name="files"/> holding an em dash, as
  /// <c>path:line</c> in file order.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> EmDashes(IEnumerable<SourceFile> files) =>
    CommentsWhere(files, text => text.Contains('\u2014'));

  /// <summary>Each comment line of <paramref name="files"/> holding a <c>b</c>, <c>i</c>,
  /// <c>em</c> or <c>strong</c> tag, as <c>path:line</c> in file order.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> HtmlEmphases(
    IEnumerable<SourceFile> files
  ) => CommentsWhere(files, HtmlEmphasis.IsMatch);

  /// <summary>Each comment line of <paramref name="files"/> holding a marker glyph (star,
  /// no-entry, warning, check mark), as <c>path:line</c> in file order.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> MarkerGlyphs(
    IEnumerable<SourceFile> files
  ) => CommentsWhere(files, text => text.IndexOfAny(Markers) >= 0);

  /// <summary>Each comment line of <paramref name="files"/> holding a character above 127, as
  /// <c>path:line</c> in file order.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> NonAscii(IEnumerable<SourceFile> files) =>
    CommentsWhere(files, text => text.Any(ch => ch > 127));

  /// <summary>Each comment line of <paramref name="files"/> holding a filler opener such as "Note
  /// that", as <c>path:line</c> in file order.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> FillerOpeners(
    IEnumerable<SourceFile> files
  ) => CommentsWhere(files, Filler.IsMatch);

  /// <summary>Each doc block (a run of <c>///</c> lines) of <paramref name="files"/> over 16 lines,
  /// as <c>path:line (n lines)</c> at its first line.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> LongDocBlocks(
    IEnumerable<SourceFile> files
  ) =>
    [
      .. DocBlocks(files)
        .Where(b => b.Lines > MaxDocBlockLines)
        .Select(b => $"{b.Where} ({b.Lines} lines)"),
    ];

  /// <summary>Each doc block of <paramref name="files"/> holding more than three
  /// <c>para</c> tags, as <c>path:line (n &lt;para&gt;)</c> at its first line.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> StackedParagraphs(
    IEnumerable<SourceFile> files
  ) =>
    [
      .. DocBlocks(files)
        .Where(b => b.Paras > MaxParaPerDocBlock)
        .Select(b => $"{b.Where} ({b.Paras} <para>)"),
    ];

  /// <summary>Each run of <c>//</c> remark lines in <paramref name="files"/> over two lines, as
  /// <c>path:line (n lines)</c>; a file under <c>Migrations/</c> or named <c>Released*</c> is
  /// skipped.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> LongRemarks(
    IEnumerable<SourceFile> files
  ) =>
    [
      .. RemarkBlocks(files)
        .Where(b => b.Lines > MaxRemarkLines)
        .Select(b => $"{b.Where} ({b.Lines} lines)"),
    ];

  /// <summary>Each <c>summary</c> in <paramref name="files"/> over five lines, as <c>path:line (n
  /// lines)</c>; a file under <c>Migrations/</c> or named <c>Released*</c> is skipped.</summary>
  /// <param name="files">The sources to read.</param>
  /// <returns>The findings; empty when clean.</returns>
  public static IReadOnlyList<string> LongSummaries(
    IEnumerable<SourceFile> files
  ) =>
    [
      .. SummaryBlocks(files)
        .Where(b => b.Lines > MaxSummaryLines)
        .Select(b => $"{b.Where} ({b.Lines} lines)"),
    ];

  // Every comment line of the files, as "<relative path>:<1-based line>" plus its text.
  private static IEnumerable<(string Where, string Text)> CommentLines(
    IEnumerable<SourceFile> files
  ) =>
    from f in files
    from i in Enumerable.Range(0, f.Lines.Length)
    where CommentLine.IsMatch(f.Lines[i])
    select ($"{f.Relative}:{i + 1}", f.Lines[i]);

  private static IReadOnlyList<string> CommentsWhere(
    IEnumerable<SourceFile> files,
    Func<string, bool> breaks
  ) => [.. CommentLines(files).Where(c => breaks(c.Text)).Select(c => c.Where)];

  // Consecutive /// lines form one doc block.
  private static IEnumerable<(string Where, int Lines, int Paras)> DocBlocks(
    IEnumerable<SourceFile> files
  ) {
    foreach (var f in files) {
      int run = 0,
        start = 0,
        paras = 0;
      for (int i = 0; i <= f.Lines.Length; i++) {
        bool isDoc = i < f.Lines.Length && XmlDocLine.IsMatch(f.Lines[i]);
        if (isDoc) {
          if (run == 0)
            start = i + 1;
          run++;
          paras += Regex.Matches(f.Lines[i], "<para>").Count;
        } else if (run > 0) {
          yield return ($"{f.Relative}:{start}", run, paras);
          run = 0;
          paras = 0;
        }
      }
    }
  }

  // Migrations and the released-code registry carry version history by design.
  private static bool IsHistoryFile(string relative) =>
    relative.Contains("/Migrations/", StringComparison.Ordinal)
    || Path.GetFileName(relative)
      .StartsWith("Released", StringComparison.Ordinal);

  // Consecutive // lines that are not /// form one remark.
  private static IEnumerable<(string Where, int Lines)> RemarkBlocks(
    IEnumerable<SourceFile> files
  ) {
    foreach (var f in files) {
      if (IsHistoryFile(f.Relative))
        continue;
      int run = 0,
        start = 0;
      for (int i = 0; i <= f.Lines.Length; i++) {
        bool isRemark =
          i < f.Lines.Length
          && CommentLine.IsMatch(f.Lines[i])
          && !XmlDocLine.IsMatch(f.Lines[i]);
        if (isRemark) {
          if (run == 0)
            start = i + 1;
          run++;
        } else if (run > 0) {
          yield return ($"{f.Relative}:{start}", run);
          run = 0;
        }
      }
    }
  }

  // A <summary> spans from its opening tag's line to its closing tag's line.
  private static IEnumerable<(string Where, int Lines)> SummaryBlocks(
    IEnumerable<SourceFile> files
  ) {
    foreach (var f in files) {
      if (IsHistoryFile(f.Relative))
        continue;
      int run = 0,
        start = 0;
      for (int i = 0; i < f.Lines.Length; i++) {
        if (!XmlDocLine.IsMatch(f.Lines[i])) {
          run = 0;
          continue;
        }
        if (f.Lines[i].Contains("<summary>", StringComparison.Ordinal)) {
          run = 1;
          start = i + 1;
        } else if (run > 0) {
          run++;
        }
        if (
          run > 0
          && f.Lines[i].Contains("</summary>", StringComparison.Ordinal)
        ) {
          yield return ($"{f.Relative}:{start}", run);
          run = 0;
        }
      }
    }
  }
}
