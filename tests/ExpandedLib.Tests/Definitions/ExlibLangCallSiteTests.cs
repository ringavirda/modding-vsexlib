using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every lang key exlib's own source names by hand must exist in every locale it ships.</summary>
public class ExlibLangCallSiteTests {
  private const string Domain = "exlib";
  private static readonly string SrcDir = RepoPaths.Src(Domain);

  [Fact]
  public void Every_key_the_source_asks_for_exists_in_every_locale() {
    var missing = LangCallSites.Unresolvable(
      Domain,
      SrcDir,
      System.IO.Path.Combine(RepoPaths.Assets(Domain), "lang")
    );

    Assert.True(
      missing.Count == 0,
      $"{missing.Count} lang key(s) named in source that no locale carries - each renders as the raw "
        + "key in game:\n  "
        + string.Join("\n  ", missing)
    );
  }

  /// <summary>Guards the check above against a scan that matches nothing.</summary>
  [Fact]
  public void The_call_site_scan_finds_keys_to_check() {
    Assert.NotEmpty(LangCallSites.Keys(Domain, SrcDir));
  }

  private static IReadOnlyList<string> PlantedCallSite(string english) {
    using var files = new PlantedFiles();
    files.Write(
      "src/Planted.cs",
      "var text = Lang.Get(\"plantedcalls:greeting\");"
    );
    files.Write("lang/en.json", english);
    return LangCallSites.Unresolvable(
      "plantedcalls",
      files.Path("src"),
      files.Path("lang")
    );
  }

  [Fact]
  [PlantedDefect(typeof(LangCallSites), nameof(LangCallSites.Unresolvable))]
  public void A_key_the_source_asks_for_that_the_locale_lacks_is_reported() {
    Assert.Equal(
      ["en: plantedcalls:greeting (Planted.cs)"],
      PlantedCallSite("{ }")
    );
  }

  [Fact]
  public void A_key_the_locale_carries_passes() {
    Assert.Empty(PlantedCallSite("""{ "greeting": "Hello" }"""));
  }

  // Fails when the scan skips the per-language lookup or the localised chat message.
  [Theory]
  [InlineData("var text = Lang.GetL(code, \"plantedcalls:greeting\");")]
  [InlineData(
    "player.SendLocalisedMessage(0, \"plantedcalls:greeting\", name);"
  )]
  public void A_key_a_player_is_sent_in_their_own_language_is_scanned(
    string callSite
  ) {
    using var files = new PlantedFiles();
    files.Write("src/Planted.cs", callSite);

    Assert.Equal(
      [("Planted.cs", "plantedcalls:greeting")],
      LangCallSites.Keys("plantedcalls", files.Path("src"))
    );
  }
}
