using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="GridRecipes.Check"/> over grid recipes this assembly declares under a
/// planted domain, one breaking each grid recipe check.</summary>
public class GridRecipesTests {
  private readonly ITestOutputHelper output;

  public GridRecipesTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string Planted = "plantedgrid";
  private static readonly Assembly Here = typeof(GridRecipesTests).Assembly;

  /// <summary>Declares its recipes only under <see cref="Planted"/>, so other scans of this
  /// assembly never see them.</summary>
  private sealed class PlantedRecipes : IExRecipeDefProvider {
    public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
      domain == Planted
        ?
        [
          ExRecipeDef
            .Create(domain, "grid", "planted")
            .Grid(g =>
              g.Name("unplaced")
                .Pattern("PP")
                .Size(2, 1)
                .Ingredient("P", i => i.Item("game:plank-oak"))
                .Ingredient("G", i => i.Item("game:gear-rusty"))
                .OutputItem("game:stick")
            ),
        ]
        : [];
  }

  // Fails when Check stops running GridRecipeShapeCheck, drops the check-name
  // prefix, or counts recipes other than the domain's grid recipes.
  [Fact]
  [PlantedDefect(typeof(GridRecipes), nameof(GridRecipes.Check))]
  public void Each_check_reports_its_planted_recipe() {
    GridRecipes.Result result = GridRecipes.Check(Planted, (Planted, Here));
    foreach (string line in result.Findings)
      output.WriteLine(line);

    Assert.Equal(1, result.Recipes);
    Assert.Equal(
      [
        "GridRecipeShape: plantedgrid:recipes/grid/planted.json#0 (unplaced): key G is not in "
          + "the pattern PP",
      ],
      result.Findings
    );
  }

  // Fails when Check stops refusing a family that does not hold the domain.
  [Fact]
  public void A_family_without_the_domain_throws() =>
    Assert.Throws<System.ArgumentException>(() =>
      GridRecipes.Check(Planted, ("other", Here))
    );
}
