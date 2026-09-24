using System.Collections.Generic;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The lang-parity rule (<see cref="LangParity"/>) over exlib's own shipped lang tree.
/// </summary>
public class LangParityTests {
  private static string LangTree =>
    Path.Combine(RepoPaths.Assets("exlib"), "lang");

  [Fact]
  public void Exlibs_locales_carry_exactly_the_english_key_set_and_placeholders() {
    var offenders = LangParity.Check(LangTree);
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  [Fact]
  public void The_corpus_reaches_a_translated_locale() {
    Assert.NotEmpty(LangParity.LocaleFiles(LangTree));
  }

  private static IReadOnlyList<string> PlantedParity(string german) {
    using var files = new PlantedFiles();
    files.Write(
      "plantedparity/lang/en.json",
      """{ "greeting": "Hello {0}" }"""
    );
    files.Write("plantedparity/lang/de.json", german);
    return LangParity.Check(files.Path("plantedparity/lang"));
  }

  [Fact]
  [PlantedDefect(typeof(LangParity), nameof(LangParity.Check))]
  public void A_locale_missing_an_english_key_is_reported() {
    Assert.Equal(
      [
        "plantedparity/de.json: 1 missing, 0 extra vs en.\n  missing: greeting\n",
      ],
      PlantedParity("{ }")
    );
  }

  [Fact]
  [PlantedDefect(typeof(LangParity), nameof(LangParity.Check))]
  public void A_locale_dropping_a_placeholder_is_reported() {
    Assert.Equal(
      [
        "plantedparity/de.json: placeholder drift in 1 key(s):\n"
          + "  greeting (en:[0] loc:[])",
      ],
      PlantedParity("""{ "greeting": "Hallo" }""")
    );
  }

  [Fact]
  public void A_locale_with_the_same_keys_and_placeholders_passes() {
    Assert.Empty(PlantedParity("""{ "greeting": "Hallo {0:F0}" }"""));
  }
}
