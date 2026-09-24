using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Repo-wide comment style checks: only the mechanically unambiguous rules; judgment
/// calls stay in <c>CONTRIBUTING.md</c>.</summary>
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.EmDashes))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.HtmlEmphases))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.MarkerGlyphs))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.NonAscii))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.FillerOpeners))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.LongDocBlocks))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.StackedParagraphs))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.LongRemarks))]
[GuardOf(typeof(CommentStyle), nameof(CommentStyle.LongSummaries))]
public class CommentStyleGuards {
  // The mod's own project folders; excludes samples/ and templates/.
  private static readonly string[] ScannedFolders =
  [
    "src/ExpandedLib",
    "src/ExpandedLib.Industry",
    "src/ExpandedLib.Testing",
    "src/ExpandedLib.Generators",
    "tests/ExpandedLib.Tests",
  ];

  private static IReadOnlyList<CommentStyle.SourceFile> Sources() =>
    Premise.NotEmpty(
      CommentStyle.Sources(RepoPaths.Root, ScannedFolders),
      "C# sources"
    );

  private static void AssertNone(IReadOnlyList<string> hits, string rule) =>
    Assert.True(hits.Count == 0, CommentStyle.Report(rule, hits));

  #region Typography

  [Fact]
  public void Comments_use_a_hyphen_not_an_em_dash() =>
    AssertNone(
      CommentStyle.EmDashes(Sources()),
      "Use '-' instead of an em dash in comments."
    );

  [Fact]
  public void Xml_docs_do_not_use_html_emphasis() =>
    AssertNone(
      CommentStyle.HtmlEmphases(Sources()),
      "Drop <b>/<i>/<em>/<strong> from doc comments - state the fact plainly instead."
    );

  [Fact]
  public void Comments_carry_no_marker_glyphs() =>
    AssertNone(
      CommentStyle.MarkerGlyphs(Sources()),
      "Remove marker glyphs (star, no-entry, warning) from comments."
    );

  [Fact]
  public void Comments_are_plain_ascii() =>
    AssertNone(
      CommentStyle.NonAscii(Sources()),
      "Comments are plain ASCII: spell out units (deg C), arrows (->) and Greek letters."
    );

  [Fact]
  public void Comments_do_not_open_with_filler() =>
    AssertNone(
      CommentStyle.FillerOpeners(Sources()),
      "Drop the filler opener and state the fact directly."
    );

  #endregion

  #region Size

  [Fact]
  public void No_doc_comment_has_grown_back_into_an_essay() =>
    AssertNone(
      CommentStyle.LongDocBlocks(Sources()),
      "A doc comment over 16 lines is an essay. Keep the constraint, move the rationale to "
        + "docs/design and cite it. CONTRIBUTING.md asks for 6 lines on a class."
    );

  [Fact]
  public void No_doc_comment_stacks_more_than_three_paragraphs() =>
    AssertNone(
      CommentStyle.StackedParagraphs(Sources()),
      "More than 3 <para> blocks means the doc is arguing rather than describing. Each <para> "
        + "should state a separate constraint."
    );

  [Fact]
  public void No_remark_runs_past_two_lines() =>
    AssertNone(
      CommentStyle.LongRemarks(Sources()),
      "A // remark over 2 lines narrates. State the constraint in one line or delete it; "
        + "CONTRIBUTING.md sizes a remark at one line."
    );

  [Fact]
  public void No_summary_runs_past_five_lines() =>
    AssertNone(
      CommentStyle.LongSummaries(Sources()),
      "A <summary> over 5 lines is an essay. One sentence for a member, three lines for a class; "
        + "move the rest to docs/design and cite it."
    );

  #endregion
}
