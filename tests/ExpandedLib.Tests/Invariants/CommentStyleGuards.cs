using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

  private sealed record SourceFile(
    string Path,
    string Relative,
    string[] Lines
  );

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
        files.Add(new SourceFile(path, rel, File.ReadAllLines(path)));
      }
    }
    Assert.True(
      files.Count > 0,
      "Found no C# sources to check - the repo-root walk is wrong."
    );
    return files;
  }

  // Every comment line in the repo, as "<relative path>:<1-based line>" plus its text.
  private static IEnumerable<(string Where, string Text)> CommentLines() =>
    from f in Sources()
    from i in Enumerable.Range(0, f.Lines.Length)
    where CommentLine.IsMatch(f.Lines[i])
    select ($"{f.Relative}:{i + 1}", f.Lines[i]);

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

  [Fact]
  public void Comments_use_a_hyphen_not_an_em_dash() {
    var hits = CommentLines()
      .Where(c => c.Text.Contains('—'))
      .Select(c => c.Where)
      .ToList();
    Assert.True(
      hits.Count == 0,
      Report("Use '-' instead of an em dash in comments.", hits)
    );
  }

  [Fact]
  public void Xml_docs_do_not_use_html_emphasis() {
    var tag = new Regex(@"</?(b|i|em|strong)>", RegexOptions.IgnoreCase);
    var hits = CommentLines()
      .Where(c => tag.IsMatch(c.Text))
      .Select(c => c.Where)
      .ToList();
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
    // These read as generated noise and carry no information a sentence cannot.
    var markers = new[] { '★', '⛔', '⚠', '✅', 'ⓘ', '❗', '⭐' };
    var hits = CommentLines()
      .Where(c => c.Text.IndexOfAny(markers) >= 0)
      .Select(c => c.Where)
      .ToList();
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
    var hits = CommentLines()
      .Where(c => c.Text.Any(ch => ch > 127))
      .Select(c => c.Where)
      .ToList();
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
    var filler = new Regex(
      @"(^|\s)(Note that|It is worth noting|Importantly|Crucially|Remember that)\b",
      RegexOptions.IgnoreCase
    );
    var hits = CommentLines()
      .Where(c => filler.IsMatch(c.Text))
      .Select(c => c.Where)
      .ToList();
    Assert.True(
      hits.Count == 0,
      Report("Drop the filler opener and state the fact directly.", hits)
    );
  }

  #endregion

  #region Size

  // Consecutive /// lines form one doc block.
  private static IEnumerable<(string Where, int Lines, int Paras)> DocBlocks() {
    foreach (var f in Sources()) {
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

  [Fact]
  public void No_doc_comment_has_grown_back_into_an_essay() {
    var hits = DocBlocks()
      .Where(b => b.Lines > MaxDocBlockLines)
      .Select(b => $"{b.Where} ({b.Lines} lines)")
      .ToList();
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
    var hits = DocBlocks()
      .Where(b => b.Paras > MaxParaPerDocBlock)
      .Select(b => $"{b.Where} ({b.Paras} <para>)")
      .ToList();
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
  private static IEnumerable<(string Where, int Lines)> RemarkBlocks() {
    foreach (var f in Sources()) {
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
  private static IEnumerable<(string Where, int Lines)> SummaryBlocks() {
    foreach (var f in Sources()) {
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

  [Fact]
  public void No_remark_runs_past_two_lines() {
    var hits = RemarkBlocks()
      .Where(b => b.Lines > MaxRemarkLines)
      .Select(b => $"{b.Where} ({b.Lines} lines)")
      .ToList();
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
    var hits = SummaryBlocks()
      .Where(b => b.Lines > MaxSummaryLines)
      .Select(b => $"{b.Where} ({b.Lines} lines)")
      .ToList();
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
}
