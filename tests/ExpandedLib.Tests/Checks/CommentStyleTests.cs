using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="CommentStyle"/>'s rules over planted sources.</summary>
public class CommentStyleTests {
  private static CommentStyle.SourceFile[] Planted(
    string relative,
    params string[] lines
  ) => [new(relative, lines)];

  private static string[] Repeat(string line, int count) =>
    [.. Enumerable.Repeat(line, count)];

  // Fails when EmDashes misses an em dash in a comment or reads one in code.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.EmDashes))]
  public void An_em_dash_in_a_comment_is_named() {
    Assert.Equal(
      ["a.cs:2"],
      CommentStyle.EmDashes(
        Planted("a.cs", "// a - b", "  // a \u2014 b", "s = \"\u2014\";")
      )
    );
  }

  // Fails when HtmlEmphases misses an emphasis tag in a doc comment or names a see tag.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.HtmlEmphases))]
  public void Html_emphasis_in_a_doc_comment_is_named() {
    Assert.Equal(
      ["a.cs:1", "a.cs:3"],
      CommentStyle.HtmlEmphases(
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
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.MarkerGlyphs))]
  public void A_marker_glyph_in_a_comment_is_named() {
    Assert.Equal(
      ["a.cs:1", "a.cs:3"],
      CommentStyle.MarkerGlyphs(
        Planted("a.cs", "// \u26a0 hot", "// 5 deg C", "/// \u2605 key")
      )
    );
  }

  // Fails when NonAscii misses a degree sign in a comment or reads one in code.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.NonAscii))]
  public void A_non_ascii_character_in_a_comment_is_named() {
    Assert.Equal(
      ["a.cs:2"],
      CommentStyle.NonAscii(
        Planted("a.cs", "s = \"\u00b0\";", "// 5 \u00b0C", "// 5 deg C")
      )
    );
  }

  // Fails when FillerOpeners misses a filler opener in a comment or names one inside a word.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.FillerOpeners))]
  public void A_filler_opener_is_named() {
    Assert.Equal(
      ["a.cs:1"],
      CommentStyle.FillerOpeners(
        Planted("a.cs", "// note that it runs", "// denote that")
      )
    );
  }

  // Fails when LongDocBlocks names a 16-line doc block or misses a 17-line one.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.LongDocBlocks))]
  public void A_doc_block_over_sixteen_lines_is_named() {
    Assert.Equal(
      ["a.cs:18 (17 lines)"],
      CommentStyle.LongDocBlocks(
        Planted(
          "a.cs",
          [.. Repeat("/// x", 16), "int a;", .. Repeat("  /// x", 17)]
        )
      )
    );
  }

  // Fails when StackedParagraphs names three para tags or misses four in one doc block.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.StackedParagraphs))]
  public void A_doc_block_with_four_paragraphs_is_named() {
    Assert.Equal(
      ["a.cs:3 (4 <para>)"],
      CommentStyle.StackedParagraphs(
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
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.LongRemarks))]
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
      CommentStyle.LongRemarks([
        new("a.cs", lines),
        new("src/Migrations/M.cs", lines),
      ])
    );
  }

  // Fails when LongSummaries names a five-line summary, misses a six-line one, or reads a file
  // named Released.
  [Fact]
  [PlantedDefect(typeof(CommentStyle), nameof(CommentStyle.LongSummaries))]
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
      CommentStyle.LongSummaries([
        new("a.cs", lines),
        new("src/ReleasedCodes.cs", lines),
      ])
    );
  }
}
