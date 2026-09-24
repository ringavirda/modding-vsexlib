using System.Collections.Generic;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The literal lang-key rule (<see cref="LangKeys"/>) over exlib's own source: <c>src/</c> plus the
/// source generators, the same non-test C# surface the old single <c>src/</c> tree held.
/// </summary>
public class LangKeyResolutionTests {
  private static readonly string[] SourceRoots =
  [
    RepoPaths.Src("exlib"),
    Path.Combine(RepoPaths.Root, "src/ExpandedLib.Generators"),
  ];

  [Fact]
  public void Every_literal_lang_key_resolves_in_english() {
    var missing = LangKeys.Check(
      SourceRoots,
      Path.Combine(RepoPaths.Assets("exlib"), "lang")
    );

    Assert.True(
      missing.Count == 0,
      $"{missing.Count} lang key(s) render as their own code in game: "
        + string.Join("; ", missing)
    );
  }

  [Fact]
  public void The_scan_finds_the_calls_it_is_meant_to_guard() {
    // A regex that stops matching turns the guard above into an unconditional pass.
    Assert.NotEmpty(LangKeys.Literals(SourceRoots));
  }

  private static IReadOnlyList<string> PlantedKey(string english) {
    using var files = new PlantedFiles();
    files.Write(
      "src/Planted.cs",
      "var text = Lang.Get(\"plantedkeys:greeting\");"
    );
    files.Write("plantedkeys/lang/en.json", english);
    return LangKeys.Check([files.Path("src")], files.Path("plantedkeys/lang"));
  }

  [Fact]
  [PlantedDefect(typeof(LangKeys), nameof(LangKeys.Check))]
  public void A_literal_key_english_lacks_is_reported() {
    string finding = Assert.Single(PlantedKey("{ }"));

    Assert.StartsWith("plantedkeys:greeting (", finding);
    Assert.EndsWith("/src/Planted.cs)", finding);
  }

  [Fact]
  public void A_literal_key_english_carries_passes() {
    Assert.Empty(PlantedKey("""{ "plantedkeys:greeting": "Hello" }"""));
  }
}
