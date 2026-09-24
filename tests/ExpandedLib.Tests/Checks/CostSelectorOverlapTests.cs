using System.Collections.Generic;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="CostSelectorOverlap.Overlaps"/> over planted two-entry catalogues.</summary>
public class CostSelectorOverlapTests {
  private static readonly string[] Codes =
  [
    "test:plate-iron",
    "test:plate-copper",
  ];

  private static RecipeCostEntry Entry(string type, string match) =>
    new() { Type = type, Match = match };

  [Fact]
  [PlantedDefect(
    typeof(CostSelectorOverlap),
    nameof(CostSelectorOverlap.Overlaps)
  )]
  public void Two_grid_selectors_matching_one_code_are_reported() {
    var catalogue = new Dictionary<string, RecipeCostEntry> {
      ["plates"] = Entry("grid", "test:plate-*"),
      ["ironplate"] = Entry("grid", "test:plate-iron"),
    };

    Assert.Equal(
      [
        "'plates' (grid test:plate-*) and 'ironplate' (grid test:plate-iron) "
          + "both match: test:plate-iron",
      ],
      CostSelectorOverlap.Overlaps(catalogue, Codes)
    );
  }

  [Fact]
  public void Disjoint_selectors_or_selectors_of_different_types_pass() {
    var disjoint = new Dictionary<string, RecipeCostEntry> {
      ["copper"] = Entry("grid", "test:plate-copper"),
      ["iron"] = Entry("grid", "test:plate-iron"),
    };
    var otherType = new Dictionary<string, RecipeCostEntry> {
      ["plates"] = Entry("grid", "test:plate-*"),
      ["ironplate"] = Entry("rcc", "test:plate-iron"),
    };

    Assert.Empty(CostSelectorOverlap.Overlaps(disjoint, Codes));
    Assert.Empty(CostSelectorOverlap.Overlaps(otherType, Codes));
  }
}
