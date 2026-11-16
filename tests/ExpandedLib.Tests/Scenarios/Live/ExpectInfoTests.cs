#if GAME_GE_1_22
using System;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The live <c>expect info</c> reader: the lines it splits and the pattern it matches them with
/// (setups.md, The live runner).</summary>
public class ExpectInfoTests
{
  /// <summary>Fails when the live runner refuses <c>expect info</c> again, with or without a control.</summary>
  [Fact]
  public void Expect_info_is_accepted_by_the_live_runner()
  {
    LiveFixture.Case("""[{ "expect": { "at": [0, 0, 0], "info": "Fuel" } }]""");
    LiveFixture.Case(
      """[{ "expect": { "at": [0, 0, 0], "info": "Fuel", "control": "door" } }]"""
    );
  }

  /// <summary>Fails when the lines are not split on newlines, trimmed or stripped of empty ones.</summary>
  [Fact]
  public void Info_lines_are_split_trimmed_and_non_empty()
  {
    Assert.Equal(
      ["Fuel: 3", "Lit"],
      ExpectStep.InfoLines("  Fuel: 3 \n\n\tLit\n")
    );
  }

  /// <summary>Fails when the pattern is anchored, matched as a literal, or read for one line instead of
  /// any, or when a pattern that matches no line returns one.</summary>
  [Fact]
  public void Info_matches_any_line_by_regex()
  {
    string[] lines = ["Fuel: 3", "Lit: yes"];
    Assert.Equal("Lit: yes", ExpectStep.InfoMatch(lines, @"^Lit: \w+$"));
    Assert.Equal("Fuel: 3", ExpectStep.InfoMatch(lines, "uel"));
    Assert.Null(ExpectStep.InfoMatch(lines, "Coke"));
    Assert.Null(ExpectStep.InfoMatch(lines, "Fuel: 3."));
    Assert.ThrowsAny<ArgumentException>(() => ExpectStep.InfoMatch(lines, "("));
  }
}
#endif
