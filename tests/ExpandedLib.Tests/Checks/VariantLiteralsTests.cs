using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="VariantLiterals"/> over planted sources and variant groups.</summary>
public class VariantLiteralsTests {
  private static VariantLiterals.Result Run(string source) {
    using var files = new PlantedFiles();
    files.Write("src/Planted.cs", source);
    return VariantLiterals.Check(
      [
        ExBlockDef
          .Create("plantedvariant", "stack")
          .VariantGroup("side", "n", "e", "s", "w")
          .VariantGroup("size", "small", "large"),
        ExBlockDef
          .Create("plantedvariant", "open")
          .VariantGroupFromProperties("type", "game:worldproperties/x"),
      ],
      files.Path("src")
    );
  }

  [Fact]
  [PlantedDefect(typeof(VariantLiterals), nameof(VariantLiterals.Check))]
  public void A_literal_that_is_no_declared_state_is_reported() {
    VariantLiterals.Result result = Run(
      "bool north = Variant[\"side\"] == \"north\";"
    );

    Assert.Equal(1, result.Comparisons);
    Assert.Equal(
      [
        "Planted.cs: Variant[\"side\"] against \"north\": not a state of side, which declares e, n, s, w",
      ],
      result.Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(VariantLiterals), nameof(VariantLiterals.Check))]
  public void A_variant_key_no_definition_declares_is_reported() {
    VariantLiterals.Result result = Run(
      "bool big = Block.Variant[\"scale\"] == \"large\";"
    );

    Assert.Equal(
      [
        "Planted.cs: Variant[\"scale\"] against \"large\": no definition declares that variant group",
      ],
      result.Findings
    );
  }

  [Theory]
  [InlineData("Variant[\"side\"] != \"x\"")]
  [InlineData("\"x\" == Block.Variant[\"side\"]")]
  [InlineData("Variant?[\"side\"] is \"x\"")]
  [InlineData("Variant[\"side\"] is not \"x\"")]
  [InlineData("Variant[\"side\"] is \"n\" or \"x\"")]
  [InlineData("Variant?[\"side\"] switch { \"n\" => 0, \"x\" => 1, _ => 2 }")]
  public void Every_comparison_form_is_read(string expression) {
    VariantLiterals.Result result = Run($"var v = {expression};");

    Assert.Single(result.Findings);
    Assert.Contains("against \"x\"", result.Findings[0]);
  }

  [Theory]
  [InlineData("Variant[\"side\"] == \"n\"")]
  [InlineData("Variant?[\"size\"] != \"large\"")]
  [InlineData("Variant?[\"type\"] == \"anything\"")]
  [InlineData("Variant?[\"side\"] switch { \"n\" => 0, \"w\" => 1, _ => 2 }")]
  public void A_declared_state_and_an_open_group_pass(string expression) {
    VariantLiterals.Result result = Run($"var v = {expression};");

    Assert.True(result.Comparisons > 0);
    Assert.Empty(result.Findings);
  }

  // Fails when a comment line is read as a comparison.
  [Fact]
  public void A_comparison_in_a_comment_line_is_not_read() {
    VariantLiterals.Result result = Run(
      "// was Variant[\"side\"] == \"north\"\nint a = 1;"
    );

    Assert.Equal(0, result.Comparisons);
  }

  // Fails when the switch scan stops at the first closing brace of an arm's body.
  [Fact]
  public void An_arm_after_a_braced_arm_is_read() {
    VariantLiterals.Result result = Run(
      "var v = Variant?[\"side\"] switch { \"n\" => new { A = 1 }, \"x\" => null, _ => null };"
    );

    Assert.Single(result.Findings);
  }
}
