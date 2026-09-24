using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
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
  private const string Wild = "plantedrecipeswild";
  private const string Rocks = "plantedrecipesrocks";
  private static readonly Assembly Here = typeof(RecipeCodesTests).Assembly;

  private static ExRecipeDef Output(string domain, string code) =>
    ExRecipeDef
      .Create(domain, "grid", "output")
      .Add(
        new JObject {
          ["ingredients"] = new JObject {
            ["P"] = new JObject { ["type"] = "item", ["code"] = "game:stick" },
          },
          ["output"] = new JObject { ["type"] = "block", ["code"] = code },
        }
      );

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
      domain switch {
        Missing or Present or Wild =>
        [
          ExBlockDef
            .Create(domain, "crate")
            .VariantGroup("wood", "oak", "pine"),
        ],
        Rocks =>
        [
          ExBlockDef
            .Create(domain, "slab")
            .VariantGroupFromProperties("rock", "block/rock"),
        ],
        _ => [],
      };
  }

  /// <summary>Declares its recipe only under <see cref="Missing"/>, where it asks for a birch
  /// crate, and <see cref="Present"/>.</summary>
  private sealed class CrateRecipe : IExRecipeDefProvider {
    public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
      domain switch {
        Missing => [Crates(domain, "oak", "birch")],
        Present => [Crates(domain, "oak", "pine")],
        Wild => [Output(domain, $"{domain}:crate-*")],
        Rocks => [Output(domain, $"{domain}:slab-andesite")],
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

  // Fails when RecipeCodesCheck.Unresolvable stops reporting an output no registered code matches.
  [Fact]
  [PlantedDefect(typeof(RecipeCodesCheck), nameof(RecipeCodesCheck.Run))]
  public void The_harness_and_the_check_report_the_same_output() {
    const string finding =
      "plantedrecipes:recipes/grid/crates.json: plantedrecipes:crate-birch";

    Assert.Equal(
      [finding],
      RecipeCodes
        .UnresolvableOutputs(Missing, Here)
        .Select(u => $"{u.RecipePath}: {u.Code}")
    );
    Assert.Equal(
      [finding],
      RecipeCodesCheck
        .Run(new AssemblyCheckSource((Missing, Here)), Missing)
        .Errors
    );
  }

  // Fails when RecipeCodesCheck.Unresolvable also accepts an output that, read as a pattern,
  // matches a registered code.
  [Fact]
  [PlantedDefect(typeof(RecipeCodes), nameof(RecipeCodes.UnresolvableOutputs))]
  public void A_wildcard_output_is_reported() {
    Assert.Equal(
      [
        new RecipeCodes.Unresolvable(
          "plantedrecipeswild:recipes/grid/output.json",
          "plantedrecipeswild:crate-*"
        ),
      ],
      RecipeCodes.UnresolvableOutputs(Wild, Here)
    );
  }

  // Fails when AssemblyCheckSource.BlockCodes ignores PropertyGroupsAsWildcard and yields only the
  // sampled rock.
  [Fact]
  public void An_output_naming_any_state_of_a_worldproperty_group_passes() {
    Assert.Empty(RecipeCodes.UnresolvableOutputs(Rocks, Here));
  }
}
