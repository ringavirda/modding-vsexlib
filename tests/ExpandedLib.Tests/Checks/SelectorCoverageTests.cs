using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="SelectorCoverage.Check"/> over planted golden blocktypes resolved against
/// exlib's own golden corpus.</summary>
public class SelectorCoverageTests {
  private static (string Path, PlantedFiles Files) Lamp(
    string shapeByType,
    string groupBy
  ) {
    var files = new PlantedFiles();
    string path = files.Write(
      "goldens/exlib/blocktypes/lamp.json",
      $$"""
      {
        "code": "lamp",
        "variantgroups": [{ "code": "color", "states": ["red", "blue"] }],
        "shapeByType": { "{{shapeByType}}": { "base": "exlib:block/empty" } },
        "attributes": { "handbook": { "groupBy": ["{{groupBy}}"] } }
      }
      """
    );
    return (path, files);
  }

  [Fact]
  [PlantedDefect(typeof(SelectorCoverage), nameof(SelectorCoverage.Check))]
  public void A_variant_no_shape_pattern_matches_is_reported() {
    var (path, files) = Lamp("*-red", "structurefiller");
    using (files) {
      var (shapeByType, groupBy) = SelectorCoverage.Check(path);

      Assert.Equal(
        [$"{path}: 1 variant(s) match no shape pattern [*-red]: lamp-blue"],
        shapeByType
      );
      Assert.Empty(groupBy);
    }
  }

  [Fact]
  [PlantedDefect(typeof(SelectorCoverage), nameof(SelectorCoverage.Check))]
  public void A_handbook_selector_matching_no_shipped_code_is_reported() {
    var (path, files) = Lamp("lamp-*", "nosuchblock-*");
    using (files) {
      var (shapeByType, groupBy) = SelectorCoverage.Check(path);

      Assert.Empty(shapeByType);
      Assert.Equal(
        [
          $"{path}: 1 handbook groupBy selector(s) match no shipped block code (resolved "
            + "against domain exlib): nosuchblock-*",
        ],
        groupBy
      );
    }
  }

  [Fact]
  public void Covered_variants_and_a_selector_naming_a_shipped_code_pass() {
    var (path, files) = Lamp("lamp-*", "structurefiller");
    using (files) {
      var (shapeByType, groupBy) = SelectorCoverage.Check(path);

      Assert.Empty(shapeByType);
      Assert.Empty(groupBy);
    }
  }
}
