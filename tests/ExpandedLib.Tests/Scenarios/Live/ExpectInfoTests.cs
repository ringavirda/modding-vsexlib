#if GAME_GE_1_22
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The <c>expect info</c> reader both runners share: the lines it splits and the patterns it
/// matches them with (setups.md, The live runner).</summary>
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
      SetupInfoExpect.Lines("  Fuel: 3 \n\n\tLit\n")
    );
  }

  /// <summary>Fails when the pattern is anchored, matched as a literal, or read for one line instead of
  /// any, or when a pattern that matches no line returns one.</summary>
  [Fact]
  public void Info_matches_any_line_by_regex()
  {
    string[] lines = ["Fuel: 3", "Lit: yes"];
    Assert.Equal("Lit: yes", SetupInfoExpect.Match(lines, @"^Lit: \w+$"));
    Assert.Equal("Fuel: 3", SetupInfoExpect.Match(lines, "uel"));
    Assert.Null(SetupInfoExpect.Match(lines, "Coke"));
    Assert.Null(SetupInfoExpect.Match(lines, "Fuel: 3."));
  }

  /// <summary>Fails when a key's template is matched unanchored, a placeholder is read as literal text, or
  /// a line of a two-line template is matched apart from the line after it.</summary>
  [Fact]
  public void A_lang_info_matches_its_template_lines_in_a_run()
  {
    JToken cold = JObject.Parse("""{ "lang": "forge-contentsandtemp-cold" }""");
    string Unformatted(string key) =>
      key == "forge-contentsandtemp-cold"
        ? "Contents: {0}x {1}\nTemperature: Cold"
        : key;

    Assert.Equal(
      "Contents: 4x Coke | Temperature: Cold",
      SetupInfoExpect.Match(
        ["Fuel", "Contents: 4x Coke", "Temperature: Cold"],
        cold,
        Unformatted
      )
    );
    Assert.Null(
      SetupInfoExpect.Match(
        ["Contents: 4x Coke", "Fuel", "Temperature: Cold"],
        cold,
        Unformatted
      )
    );
    Assert.Null(
      SetupInfoExpect.Match(
        ["Contents: 4x Coke", "Temperature: Cold!"],
        cold,
        Unformatted
      )
    );
  }

  /// <summary>Fails when a key the language lacks is not matched as itself, the line the harness's
  /// echoing language writes, or is matched inside a longer line.</summary>
  [Fact]
  public void A_lang_info_the_language_lacks_matches_the_key_itself()
  {
    JToken key = JObject.Parse("""{ "lang": "forge-contentsandtemp-cold" }""");
    Assert.Equal(
      "forge-contentsandtemp-cold",
      SetupInfoExpect.Match(["Fuel", "forge-contentsandtemp-cold"], key, k => k)
    );
    Assert.Null(
      SetupInfoExpect.Match(["forge-contentsandtemp-cold-x"], key, k => k)
    );
    Assert.Null(
      SetupInfoExpect.Match(["a forge-contentsandtemp-cold"], key, k => k)
    );
  }

  /// <summary>Fails when a pattern that is no regex, or an info that is neither a string nor an object,
  /// is let through.</summary>
  [Fact]
  public void An_info_that_is_no_regex_is_refused()
  {
    var at = new SetupJson.Where("f.json", "steps[0].info");
    Assert.Contains(
      "is not a regex",
      Assert
        .Throws<SetupFileException>(() =>
          SetupInfoExpect.Validate(JValue.CreateString("("), at)
        )
        .Message
    );
    Assert.Contains(
      "expected a string",
      Assert
        .Throws<SetupFileException>(() =>
          SetupInfoExpect.Validate(new JValue(5), at)
        )
        .Message
    );
  }
}
#endif
