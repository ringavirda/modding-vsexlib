using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="OpenLayoutCells"/> over planted one-column layouts: a core <c>C</c> under
/// an open shaft, marked or not, capped by a roof or not.</summary>
public class OpenLayoutCellsTests(ITestOutputHelper output) {
  private static readonly Dictionary<char, string> Glyphs = new() {
    ['c'] = "plantedsnow:shaft*",
    ['#'] = "game:brick*",
    ['a'] = "game:air",
    ['h'] = "*:@(air|coalpile)",
  };

  // c, the core, and #, a brick, are solid; a is air, h an air-first alternation. A capital glyph
  // carries nosnow. Each argument is one layer's single cell, layer 0 first.
  private OpenLayoutCells.Result Column(params char[] layers) {
    ExBlockDef def = ExBlockDef
      .Create("plantedsnow", "shaft")
      .MultiblockLayout(s => {
        s.Origin(0, 0);
        foreach (char glyph in layers.Distinct()) {
          s.Legend(glyph, Glyphs[char.ToLowerInvariant(glyph)]);
          if (char.IsUpper(glyph))
            s.Role(glyph, CellRoles.NoSnow);
        }
        for (int y = 0; y < layers.Length; y++)
          s.Layer(y, layers[y].ToString());
      });
    OpenLayoutCells.Result result = OpenLayoutCells.Check([def]);
    foreach (string line in result.Findings)
      output.WriteLine(line);
    return result;
  }

  [Fact]
  [PlantedDefect(typeof(OpenLayoutCells), nameof(OpenLayoutCells.Check))]
  public void An_unmarked_cell_snow_settles_in_is_reported() {
    OpenLayoutCells.Result result = Column('C', 'h', 'a');

    Assert.Equal(1, result.Layouts);
    Assert.Equal(2, result.Cells);
    Assert.Equal(
      [
        "plantedsnow/blocktypes/shaft.json: (0,1,0) snow settles in carries no nosnow",
      ],
      result.Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(OpenLayoutCells), nameof(OpenLayoutCells.Check))]
  public void An_unmarked_cell_snow_lies_on_is_reported() =>
    Assert.Equal(
      [
        "plantedsnow/blocktypes/shaft.json: (0,1,0) snow lies on carries no nosnow",
      ],
      Column('#', 'c', 'A', 'a').Findings
    );

  [Theory]
  [InlineData("CHa")]
  [InlineData("CA#")]
  [InlineData("aaa")]
  public void A_marked_shaft_a_roofed_column_and_a_column_of_air_pass(
    string layers
  ) => Assert.Empty(Column(layers.ToCharArray()).Findings);

  [Fact]
  public void A_roofed_column_requires_nothing() =>
    Assert.Equal(0, Column('c', 'a', '#').Cells);

  [Fact]
  public void A_definition_without_a_layout_is_not_read() =>
    Assert.Equal(
      0,
      OpenLayoutCells.Check([ExBlockDef.Create("plantedsnow", "plain")]).Layouts
    );
}
