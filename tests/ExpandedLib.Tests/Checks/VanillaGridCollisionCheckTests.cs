using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="VanillaGridCollisionCheck"/> over a planted domain's grid recipes and a
/// planted vanilla one of the same input.</summary>
public class VanillaGridCollisionCheckTests {
  private readonly ITestOutputHelper output;

  public VanillaGridCollisionCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string Own = "stub:recipes/grid/planted.json";
  private const string Vanilla = "game:recipes/grid/trough.json";

  private static string Recipe(string name) =>
    $$"""
      {
        "name": "{{name}}", "ingredientPattern": "P_P,PPP", "width": 3, "height": 2,
        "ingredients": { "P": { "type": "item", "code": "game:plank-*" } },
        "output": { "type": "block", "code": "stub:{{name}}" }
      }
      """;

  private static LoadedStubGame Game() =>
    new(
      new RecipeStubSource()
        .Recipe(Own, Recipe("rack"))
        .Recipe(Own, Recipe("shelf"))
        .Recipe(Vanilla, Recipe("trough"))
    );

  private IReadOnlyList<string> Findings(string domain) {
    IReadOnlyList<string> found = VanillaGridCollisionCheck
      .Run(Game(), domain)
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);
    return found;
  }

  // Fails when Run stops comparing a domain's recipes with vanilla's, or starts comparing the
  // domain's own recipes with each other.
  [Fact]
  [PlantedDefect(
    typeof(VanillaGridCollisionCheck),
    nameof(VanillaGridCollisionCheck.Run)
  )]
  public void A_recipe_of_a_vanilla_recipes_input_is_reported() =>
    Assert.Equal(
      [
        $"{Own}#0 (rack) and recipes/grid/trough.json#0 (trough) match the same input",
        $"{Own}#1 (shelf) and recipes/grid/trough.json#0 (trough) match the same input",
      ],
      Findings("stub")
    );

  // Fails when the game domain's own run compares vanilla with itself.
  [Fact]
  public void The_game_domain_reports_nothing() =>
    Assert.Empty(Findings("game"));
}
