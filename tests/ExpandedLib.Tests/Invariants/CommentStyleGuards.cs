using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Repo-wide comment style checks: only the mechanically unambiguous rules; judgment
/// calls stay in <c>CONTRIBUTING.md</c>.</summary>
public class CommentStyleGuards {
  #region Corpus

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

  /// <summary>A C# source: its path relative to the repository root, and its lines.</summary>
  public sealed record SourceFile(string Relative, string[] Lines);

  // The mod's own project folders; excludes samples/ and templates/.
  private static readonly string[] ScannedFolders =
  [
    "src/ExpandedLib",
    "src/ExpandedLib.Industry",
    "src/ExpandedLib.Testing",
    "src/ExpandedLib.Generators",
    "tests/ExpandedLib.Tests",
  ];

  private static IReadOnlyList<SourceFile> Sources() {
    string root = RepoRoot();
    var files = new List<SourceFile>();
    foreach (string folder in ScannedFolders) {
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
    return Premise.NotEmpty(files, "C# sources");
  }

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

  private static string Report(string rule, IEnumerable<string> hits) {
    var list = hits.ToList();
    return $"{rule}\n  {list.Count} violation(s):\n"
      + string.Join("\n", list.Take(25).Select(h => "    " + h))
      + (list.Count > 25 ? $"\n    ... and {list.Count - 25} more" : "");
  }

  private static string RepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (
      dir != null && !File.Exists(Path.Combine(dir.FullName, "ExpandedLib.sln"))
    )
      dir = dir.Parent;
    return dir?.FullName
      ?? throw new InvalidOperationException(
        "Could not locate the repo root (ExpandedLib.sln) from "
          + AppContext.BaseDirectory
      );
  }

  #endregion

  #region Typography

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

  /// <summary>Each comment line of <paramref name="files"/> holding an em dash, as
  /// <c>path:line</c> in file order.</summary>
  public static IReadOnlyList<string> EmDashes(IEnumerable<SourceFile> files) =>
    CommentsWhere(files, text => text.Contains('\u2014'));

  /// <summary>Each comment line of <paramref name="files"/> holding a <c>b</c>, <c>i</c>,
  /// <c>em</c> or <c>strong</c> tag, as <c>path:line</c> in file order.</summary>
  public static IReadOnlyList<string> HtmlEmphases(
    IEnumerable<SourceFile> files
  ) => CommentsWhere(files, HtmlEmphasis.IsMatch);

  /// <summary>Each comment line of <paramref name="files"/> holding a marker glyph (star,
  /// no-entry, warning, check mark), as <c>path:line</c> in file order.</summary>
  public static IReadOnlyList<string> MarkerGlyphs(
    IEnumerable<SourceFile> files
  ) => CommentsWhere(files, text => text.IndexOfAny(Markers) >= 0);

  /// <summary>Each comment line of <paramref name="files"/> holding a character above 127, as
  /// <c>path:line</c> in file order.</summary>
  public static IReadOnlyList<string> NonAscii(IEnumerable<SourceFile> files) =>
    CommentsWhere(files, text => text.Any(ch => ch > 127));

  /// <summary>Each comment line of <paramref name="files"/> holding a filler opener such as "Note
  /// that", as <c>path:line</c> in file order.</summary>
  public static IReadOnlyList<string> FillerOpeners(
    IEnumerable<SourceFile> files
  ) => CommentsWhere(files, Filler.IsMatch);

  [Fact]
  public void Comments_use_a_hyphen_not_an_em_dash() {
    var hits = EmDashes(Sources());
    Assert.True(
      hits.Count == 0,
      Report("Use '-' instead of an em dash in comments.", hits)
    );
  }

  [Fact]
  public void Xml_docs_do_not_use_html_emphasis() {
    var hits = HtmlEmphases(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        "Drop <b>/<i>/<em>/<strong> from doc comments - state the fact plainly instead.",
        hits
      )
    );
  }

  [Fact]
  public void Comments_carry_no_marker_glyphs() {
    var hits = MarkerGlyphs(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        "Remove marker glyphs (star, no-entry, warning) from comments.",
        hits
      )
    );
  }

  [Fact]
  public void Comments_are_plain_ascii() {
    var hits = NonAscii(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        "Comments are plain ASCII: spell out units (deg C), arrows (->) and Greek letters.",
        hits
      )
    );
  }

  [Fact]
  public void Comments_do_not_open_with_filler() {
    var hits = FillerOpeners(Sources());
    Assert.True(
      hits.Count == 0,
      Report("Drop the filler opener and state the fact directly.", hits)
    );
  }

  #endregion

  #region Size

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

  /// <summary>Each doc block of <paramref name="files"/> over 16 lines, as <c>path:line (n
  /// lines)</c> at its first line.</summary>
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
  public static IReadOnlyList<string> StackedParagraphs(
    IEnumerable<SourceFile> files
  ) =>
    [
      .. DocBlocks(files)
        .Where(b => b.Paras > MaxParaPerDocBlock)
        .Select(b => $"{b.Where} ({b.Paras} <para>)"),
    ];

  [Fact]
  public void No_doc_comment_has_grown_back_into_an_essay() {
    var hits = LongDocBlocks(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        $"A doc comment over {MaxDocBlockLines} lines is an essay. Keep the constraint, move the "
          + "rationale to docs/design and cite it. CONTRIBUTING.md asks for 6 lines on a class.",
        hits
      )
    );
  }

  [Fact]
  public void No_doc_comment_stacks_more_than_three_paragraphs() {
    var hits = StackedParagraphs(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        $"More than {MaxParaPerDocBlock} <para> blocks means the doc is arguing rather than "
          + "describing. Each <para> should state a separate constraint.",
        hits
      )
    );
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

  /// <summary>Each run of <c>//</c> remark lines in <paramref name="files"/> over two lines, as
  /// <c>path:line (n lines)</c>; a file under <c>Migrations/</c> or named <c>Released*</c> is
  /// skipped.</summary>
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
  public static IReadOnlyList<string> LongSummaries(
    IEnumerable<SourceFile> files
  ) =>
    [
      .. SummaryBlocks(files)
        .Where(b => b.Lines > MaxSummaryLines)
        .Select(b => $"{b.Where} ({b.Lines} lines)"),
    ];

  [Fact]
  public void No_remark_runs_past_two_lines() {
    var hits = LongRemarks(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        $"A // remark over {MaxRemarkLines} lines narrates. State the constraint in one line or "
          + "delete it; CONTRIBUTING.md sizes a remark at one line.",
        hits
      )
    );
  }

  [Fact]
  public void No_summary_runs_past_five_lines() {
    var hits = LongSummaries(Sources());
    Assert.True(
      hits.Count == 0,
      Report(
        $"A <summary> over {MaxSummaryLines} lines is an essay. One sentence for a member, three "
          + "lines for a class; move the rest to docs/design and cite it.",
        hits
      )
    );
  }

  #endregion

  #region Planted

  private static SourceFile[] Planted(string relative, params string[] lines) =>
    [new(relative, lines)];

  private static string[] Repeat(string line, int count) =>
    [.. Enumerable.Repeat(line, count)];

  // Fails when EmDashes misses an em dash in a comment or reads one in code.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(EmDashes))]
  public void An_em_dash_in_a_comment_is_named() {
    Assert.Equal(
      ["a.cs:2"],
      EmDashes(
        Planted("a.cs", "// a - b", "  // a \u2014 b", "s = \"\u2014\";")
      )
    );
  }

  // Fails when HtmlEmphases misses an emphasis tag in a doc comment or names a see tag.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(HtmlEmphases))]
  public void Html_emphasis_in_a_doc_comment_is_named() {
    Assert.Equal(
      ["a.cs:1", "a.cs:3"],
      HtmlEmphases(
        Planted(
          "a.cs",
          "/// <B>loud</B>",
          "/// <see cref=\"X\"/> and <c>x</c>",
          "// an </strong> close"
        )
      )
    );
  }

  // Fails when MarkerGlyphs misses a warning sign or a star in a comment.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(MarkerGlyphs))]
  public void A_marker_glyph_in_a_comment_is_named() {
    Assert.Equal(
      ["a.cs:1", "a.cs:3"],
      MarkerGlyphs(
        Planted("a.cs", "// \u26a0 hot", "// 5 deg C", "/// \u2605 key")
      )
    );
  }

  // Fails when NonAscii misses a degree sign in a comment or reads one in code.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(NonAscii))]
  public void A_non_ascii_character_in_a_comment_is_named() {
    Assert.Equal(
      ["a.cs:2"],
      NonAscii(Planted("a.cs", "s = \"\u00b0\";", "// 5 \u00b0C", "// 5 deg C"))
    );
  }

  // Fails when FillerOpeners misses a filler opener in a comment or names one inside a word.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(FillerOpeners))]
  public void A_filler_opener_is_named() {
    Assert.Equal(
      ["a.cs:1"],
      FillerOpeners(Planted("a.cs", "// note that it runs", "// denote that"))
    );
  }

  // Fails when LongDocBlocks names a 16-line doc block or misses a 17-line one.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(LongDocBlocks))]
  public void A_doc_block_over_sixteen_lines_is_named() {
    Assert.Equal(
      ["a.cs:18 (17 lines)"],
      LongDocBlocks(
        Planted(
          "a.cs",
          [.. Repeat("/// x", 16), "int a;", .. Repeat("  /// x", 17)]
        )
      )
    );
  }

  // Fails when StackedParagraphs names three para tags or misses four in one doc block.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(StackedParagraphs))]
  public void A_doc_block_with_four_paragraphs_is_named() {
    Assert.Equal(
      ["a.cs:3 (4 <para>)"],
      StackedParagraphs(
        Planted(
          "a.cs",
          "/// <para>a</para><para>b</para><para>c</para>",
          "int a;",
          "/// <para>a</para><para>b</para>",
          "/// <para>c</para><para>d</para>"
        )
      )
    );
  }

  // Fails when LongRemarks names a two-line remark, counts a doc line into one, misses a
  // three-line remark, or reads a Migrations file.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(LongRemarks))]
  public void A_remark_over_two_lines_is_named() {
    string[] lines =
    [
      "// a",
      "// b",
      "/// c",
      "int a;",
      "  // a",
      "  // b",
      "  // c",
    ];

    Assert.Equal(
      ["a.cs:5 (3 lines)"],
      LongRemarks([new("a.cs", lines), new("src/Migrations/M.cs", lines)])
    );
  }

  // Fails when LongSummaries names a five-line summary, misses a six-line one, or reads a file
  // named Released.
  [Fact]
  [PlantedDefect(typeof(CommentStyleGuards), nameof(LongSummaries))]
  public void A_summary_over_five_lines_is_named() {
    string[] lines =
    [
      "/// <summary>a",
      .. Repeat("/// b", 3),
      "/// c</summary>",
      "int a;",
      "/// <summary>a",
      .. Repeat("/// b", 4),
      "/// c</summary>",
    ];

    Assert.Equal(
      ["a.cs:7 (6 lines)"],
      LongSummaries([new("a.cs", lines), new("src/ReleasedCodes.cs", lines)])
    );
  }

  #endregion
}
