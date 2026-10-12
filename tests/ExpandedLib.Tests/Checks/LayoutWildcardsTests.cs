using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="LayoutWildcards"/> over planted one-cell layouts.</summary>
public class LayoutWildcardsTests {
  private static LayoutWildcards.Result Cell(string code) =>
    LayoutWildcards.Check([
      ExBlockDef
        .Create("plantedwild", "stack")
        .MultiblockLayout(s =>
        {
          s.Origin(0, 0);
          s.Legend('x', code);
          s.Layer(0, "x");
        }),
    ]);

  [Fact]
  [PlantedDefect(typeof(LayoutWildcards), nameof(LayoutWildcards.Check))]
  public void A_bare_star_inside_an_alternation_is_reported() {
    LayoutWildcards.Result result = Cell("*:@(air|hearthmetal-*)");

    Assert.Equal(1, result.Layouts);
    Assert.Equal(1, result.Alternations);
    Assert.Equal(
      [
        "plantedwild/blocktypes/stack.json: *:@(air|hearthmetal-*) has * not preceded by . inside @(...)",
      ],
      result.Findings
    );
  }

  [Theory]
  [InlineData("*:@(air|hearthmetal-.*)")]
  [InlineData("game:refractorybricks-good-tier*")]
  [InlineData("*:@(air|coalpile)")]
  public void The_regex_form_and_a_glob_outside_an_alternation_pass(string code) {
    Assert.Empty(Cell(code).Findings);
  }

  // Fails when the scan stops at the first parenthesis of a nested group.
  [Fact]
  public void A_bare_star_after_a_nested_group_is_reported() {
    Assert.Single(Cell("@(claybricks-(good|bad)|brickcourse-*)").Findings);
  }

  // Fails when the scan reads past the closing parenthesis of its group.
  [Fact]
  public void A_star_after_the_group_closes_passes() {
    Assert.Empty(Cell("@(air|coalpile)-*").Findings);
  }
}
