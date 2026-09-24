using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="RecipeCodes.UnresolvableOutputs"/> over a planted grid recipe whose output
/// placeholder expands to a crate variant that is and is not defined.</summary>
public class RecipeCodesTests {
  public RecipeCodesTests() => TestModDomain.Register();

  private const string Missing = "plantedrecipes";
  private const string Present = "plantedrecipesclean";
  private static readonly Assembly Here = typeof(RecipeCodesTests).Assembly;

  private static ExRecipeDef Crates(string domain, params string[] woods) =>
    ExRecipeDef
      .Create(domain, "grid", "crates")
      .Add(
        new JObject {
          ["ingredients"] = new JObject {
            ["P"] = new JObject {
              ["type"] = "item",
              ["code"] = "game:plank-*",
              ["name"] = "wood",
              ["allowedVariants"] = new JArray(woods),
            },
          },
          ["output"] = new JObject {
            ["type"] = "block",
            ["code"] = $"{domain}:crate-{{wood}}",
          },
        }
      );

  /// <summary>Declares its crate only under <see cref="Missing"/> and <see cref="Present"/>, so
  /// other scans of this assembly never see it.</summary>
  private sealed class Crate : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain is Missing or Present
        ?
        [
          ExBlockDef
            .Create(domain, "crate")
            .VariantGroup("wood", "oak", "pine"),
        ]
        : [];
  }

  /// <summary>Declares its recipe only under <see cref="Missing"/>, where it asks for a birch
  /// crate, and <see cref="Present"/>.</summary>
  private sealed class CrateRecipe : IExRecipeDefProvider {
    public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
      domain switch {
        Missing => [Crates(domain, "oak", "birch")],
        Present => [Crates(domain, "oak", "pine")],
        _ => [],
      };
  }

  [Fact]
  [PlantedDefect(typeof(RecipeCodes), nameof(RecipeCodes.UnresolvableOutputs))]
  public void An_output_variant_no_block_defines_is_reported() {
    Assert.Equal(
      [
        new RecipeCodes.Unresolvable(
          "plantedrecipes:recipes/grid/crates.json",
          "plantedrecipes:crate-birch"
        ),
      ],
      RecipeCodes.UnresolvableOutputs(Missing, Here)
    );
  }

  [Fact]
  public void Outputs_naming_defined_variants_pass() {
    Assert.Empty(RecipeCodes.UnresolvableOutputs(Present, Here));
  }
}
