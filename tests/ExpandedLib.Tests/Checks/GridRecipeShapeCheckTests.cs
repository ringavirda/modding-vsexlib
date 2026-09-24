using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="GridRecipeShapeCheck"/> over planted grid recipes, each breaking one rule:
/// a key the pattern never places, a letter with no key, a grid larger than 3x3.</summary>
public class GridRecipeShapeCheckTests {
  private readonly ITestOutputHelper output;

  public GridRecipeShapeCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string File = "stub:recipes/grid/planted.json";

  private IReadOnlyList<string> Findings(string recipe) {
    IReadOnlyList<string> found = GridRecipeShapeCheck
      .Run(new RecipeStubSource().Recipe(File, recipe), "stub")
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);
    return found;
  }

  // Fails when Run stops reporting a key the pattern never places.
  [Fact]
  [PlantedDefect(
    typeof(GridRecipeShapeCheck),
    nameof(GridRecipeShapeCheck.Run)
  )]
  public void A_key_missing_from_the_pattern_is_reported() =>
    Assert.Equal(
      [
        "stub:recipes/grid/planted.json#0 (gearbox): key G is not in the pattern _H_,PRP",
      ],
      Findings(
        """
        {
          "name": "gearbox",
          "ingredientPattern": "_H_,PRP", "width": 3, "height": 2,
          "ingredients": {
            "P": { "type": "item", "code": "game:metalplate-iron" },
            "R": { "type": "item", "code": "game:rod-iron" },
            "G": { "type": "item", "code": "game:gear-rusty" },
            "H": { "type": "item", "code": "game:hammer-*", "isTool": true }
          },
          "output": { "type": "block", "code": "stub:gearbox" }
        }
        """
      )
    );

  // Fails when Run stops reporting a pattern letter no ingredient keys, or reads a space or an
  // underscore as a letter.
  [Fact]
  [PlantedDefect(
    typeof(GridRecipeShapeCheck),
    nameof(GridRecipeShapeCheck.Run)
  )]
  public void A_letter_with_no_key_is_reported() =>
    Assert.Equal(
      ["stub:recipes/grid/planted.json#0 (stub:crate): letter Q has no key"],
      Findings(
        """
        {
          "ingredientPattern": "P P,_Q_", "width": 3, "height": 2,
          "ingredients": { "P": { "type": "item", "code": "game:plank-oak" } },
          "output": { "type": "block", "code": "stub:crate" }
        }
        """
      )
    );

  // Fails when a recipe with neither a name nor an output stops being labelled unnamed.
  [Fact]
  public void A_recipe_with_no_name_or_output_is_labelled_unnamed() =>
    Assert.Equal(
      ["stub:recipes/grid/planted.json#0 ((unnamed)): letter Q has no key"],
      Findings("""{ "ingredientPattern": "Q", "width": 1, "height": 1 }""")
    );

  // Fails when Run stops reporting a grid wider than the crafting grid, or reads the width and
  // height case-sensitively.
  [Fact]
  [PlantedDefect(
    typeof(GridRecipeShapeCheck),
    nameof(GridRecipeShapeCheck.Run)
  )]
  public void A_grid_larger_than_three_by_three_is_reported() =>
    Assert.Equal(
      ["stub:recipes/grid/planted.json#0 (beam): grid 4x1 is larger than 3x3"],
      Findings(
        """
        {
          "Name": "beam",
          "IngredientPattern": "PPPP", "Width": 4, "Height": 1,
          "Ingredients": { "P": { "type": "item", "code": "game:plank-oak" } },
          "Output": { "type": "block", "code": "stub:beam" }
        }
        """
      )
    );

  // Fails when Run reads a recipe outside recipes/grid, or reports a placed key or a default
  // 3x3 grid.
  [Fact]
  public void A_placed_recipe_and_a_recipe_outside_the_grid_folder_pass() {
    var source = new RecipeStubSource()
      .Recipe(
        File,
        """
        {
          "ingredientPattern": "PPP,P_P,PPP",
          "ingredients": { "P": { "type": "item", "code": "game:plank-oak" } },
          "output": { "type": "block", "code": "stub:crate" }
        }
        """
      )
      .Recipe(
        "stub:recipes/smithing/planted.json",
        """{ "ingredientPattern": "", "ingredients": { "P": { "code": "game:x" } } }"""
      );

    Assert.Empty(GridRecipeShapeCheck.Run(source, "stub").Errors);
  }
}
