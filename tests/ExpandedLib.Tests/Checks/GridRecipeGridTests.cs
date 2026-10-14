using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ExlibChecks.CraftingGrid"/> and the grid checks that read it: a recipe up
/// to the declared size is accepted, and two recipes are held against each other in every grid
/// both fit in.</summary>
public sealed class GridRecipeGridTests : IDisposable {
  public GridRecipeGridTests() {
    TestModDomain.Register();
    ExlibChecks.ClearDeclarations();
  }

  public void Dispose() => ExlibChecks.ClearDeclarations();

  private const string File = "stub:recipes/grid/planted.json";
  private const string Vanilla = "game:recipes/grid/planted.json";

  private const string Station = "the workbench";
  private const string Oak = """{ "type": "item", "code": "game:plank-oak" }""";

  private static string Recipe(
    string name,
    string pattern,
    int width,
    int height
  ) =>
    $$"""
      {
        "name": "{{name}}", "ingredientPattern": "{{pattern}}",
        "width": {{width}}, "height": {{height}},
        "ingredients": { "P": {{Oak}} },
        "output": { "type": "item", "code": "stub:{{name}}" }
      }
      """;

  private static string ShapelessRecipe(
    string name,
    string pattern,
    int width,
    int height
  ) =>
    Recipe(name, pattern, width, height)
      .Replace("\"width\"", "\"shapeless\": true, \"width\"");

  private static string Cells(int width, int height, int at) =>
    string.Join(
      ",",
      Enumerable
        .Range(0, height)
        .Select(r =>
          string.Concat(
            Enumerable
              .Range(0, width)
              .Select(c => r * width + c == at ? "P" : "_")
          )
        )
    );

  private static string Member(string name) =>
    Recipe(name, Cells(5, 5, 0), 5, 5);

  private static IReadOnlyList<string> Shape(params string[] recipes) =>
    GridRecipeShapeCheck
      .Run(
        recipes.Aggregate(new RecipeStubSource(), (s, r) => s.Recipe(File, r)),
        "stub"
      )
      .Errors;

  private static IReadOnlyList<string> Collisions(params string[] recipes) =>
    GridRecipeCollisionCheck
      .Run(
        recipes.Aggregate(new RecipeStubSource(), (s, r) => s.Recipe(File, r)),
        "stub"
      )
      .Errors;

  private static string Pair(string a, string b, string tail = "") =>
    $"{File}#0 ({a}) and {File}#1 ({b}) match the same input{tail}";

  // Fails when the shape check keeps the hand grid as its bound with a larger grid declared.
  [Fact]
  public void A_five_by_five_recipe_passes_the_shape_check_with_a_five_declared() {
    ExlibChecks.CraftingGrid("stub", 5, Station);

    Assert.Empty(Shape(Member("member")));
  }

  // Fails when the shape check stops bounding a recipe by the declared grids, or names the hand
  // grid as the bound once a larger one is declared.
  [Fact]
  public void A_five_by_five_recipe_is_refused_without_a_five_declared() {
    Assert.Equal(
      [$"{File}#0 (member): grid 5x5 is larger than 3x3"],
      Shape(Member("member"))
    );

    ExlibChecks.CraftingGrid("stub", 4, Station);
    Assert.Equal(
      [$"{File}#0 (member): grid 5x5 is larger than the 4x4 grid of {Station}"],
      Shape(Member("member"))
    );
  }

  // Fails when the shape check bounds a recipe by the first larger grid declared rather than the
  // largest, or refuses it without naming that grid.
  [Fact]
  public void A_recipe_is_bounded_by_the_largest_grid_the_domain_declares() {
    ExlibChecks.CraftingGrid("stub", 7, "the mill");
    ExlibChecks.CraftingGrid("stub", 5, Station);

    Assert.Empty(Shape(Recipe("seven", Cells(7, 7, 0), 7, 7)));
    Assert.Equal(
      [$"{File}#0 (eight): grid 8x8 is larger than the 7x7 grid of the mill"],
      Shape(Recipe("eight", Cells(8, 8, 0), 8, 8))
    );
  }

  // Fails when a grid declared for another domain bounds this domain's recipes.
  [Fact]
  public void A_grid_declared_for_another_domain_does_not_bound_the_domain() {
    ExlibChecks.CraftingGrid("other", 5, Station);

    Assert.Single(Shape(Member("member")));
  }

  // Fails when the collision check judges a pair in the hand grid only, or leaves out the grid's
  // name.
  [Fact]
  public void A_member_and_a_hand_recipe_taking_one_input_collide_at_the_bench() {
    ExlibChecks.CraftingGrid("stub", 5, Station);

    Assert.Equal(
      [Pair("member", "hand", $" in the 5x5 grid of {Station}")],
      Collisions(Member("member"), Recipe("hand", "P", 1, 1))
    );
  }

  // Fails when the collision check judges a pair in a 5x5 grid no domain declared.
  [Fact]
  public void A_member_and_a_hand_recipe_do_not_collide_without_the_grid() =>
    Assert.Empty(Collisions(Member("member"), Recipe("hand", "P", 1, 1)));

  // Fails when Meets takes 3 as the grid's size whatever the grid: two recipes whose boxes sit at
  // opposite ends of a 3-wide row meet only in the 5-wide one.
  [Fact]
  public void Two_recipes_meeting_only_in_the_five_by_five_grid_are_reported_with_it() {
    string right = Recipe("right", "__P", 3, 1);
    string left = Recipe("left", "P__", 3, 1);
    Assert.Empty(Collisions(right, left));

    ExlibChecks.CraftingGrid("stub", 5, Station);
    Assert.Equal(
      [Pair("right", "left", $" in the 5x5 grid of {Station}")],
      Collisions(right, left)
    );
  }

  // Fails when Collisions takes the grids declared for the checked domain, not every domain's.
  [Fact]
  public void Two_recipes_of_a_domain_meeting_at_another_domains_bench_are_reported_with_it() {
    ExlibChecks.CraftingGrid("stub", 5, Station);
    const string other = "other:recipes/grid/planted.json";
    var source = new RecipeStubSource()
      .Recipe(other, Recipe("right", "__P", 3, 1))
      .Recipe(other, Recipe("left", "P__", 3, 1));
    string line =
      $"{other}#0 (right) and {other}#1 (left) match the same input in the 5x5 grid of {Station}";

    Assert.Equal([line], GridRecipeCollisionCheck.Run(source, "other").Errors);
    Assert.Empty(GridRecipeShapeCheck.Run(source, "other").Errors);
  }

  // Fails when the pair is judged without Fits on the first recipe.
  [Fact]
  public void A_shapeless_recipe_wider_than_the_grid_does_not_collide_with_a_hand_recipe() =>
    Assert.Empty(
      GridRecipeCollisionCheck
        .Run(
          new RecipeStubSource()
            .Recipe(File, ShapelessRecipe("wide", "P___", 4, 1))
            .Recipe(File, Recipe("hand", "P", 1, 1)),
          "stub"
        )
        .Errors
    );

  // Fails when a pair colliding in the hand grid is reported again for a larger grid, or its line
  // names a larger grid.
  [Fact]
  public void A_pair_colliding_in_the_hand_grid_is_reported_once_without_a_grid_name() {
    ExlibChecks.CraftingGrid("stub", 5, Station);

    Assert.Equal(
      [Pair("one", "two")],
      Collisions(Recipe("one", "P", 1, 1), Recipe("two", "P", 1, 1))
    );
  }

  // Fails when the vanilla collision check judges the pair in the hand grid only.
  [Fact]
  public void A_member_and_a_vanilla_recipe_collide_at_the_bench() {
    ExlibChecks.CraftingGrid("stub", 5, Station);
    var game = new LoadedStubGame(
      new RecipeStubSource()
        .Recipe(File, Member("member"))
        .Recipe(Vanilla, Recipe("trough", "P", 1, 1))
    );

    Assert.Equal(
      [
        $"{File}#0 (member) and recipes/grid/planted.json#0 (trough) match the same input in the 5x5 grid of {Station}",
      ],
      VanillaGridCollisionCheck.Run(game, "stub").Errors
    );
  }

  // Fails when Grids lists a grid of the hand grid's size or smaller, another domain's grid, a
  // repeat, or the larger grids out of order.
  [Fact]
  public void The_grids_of_a_domain_are_the_hand_grid_and_its_larger_ones_smallest_first() {
    ExlibChecks.CraftingGrid("stub", 7, "the mill");
    ExlibChecks.CraftingGrid("stub", 5, Station);
    ExlibChecks.CraftingGrid("stub", 5, Station);
    ExlibChecks.CraftingGrid("stub", 2, "the tray");
    ExlibChecks.CraftingGrid("other", 9, "their bench");

    Assert.Equal(
      ["3 hand", $"5 {Station}", "7 the mill"],
      ExlibChecks.Grids("stub").Select(g => $"{g.Size} {g.Station}")
    );
    Assert.Equal(
      ["3 hand"],
      ExlibChecks.Grids("none").Select(g => $"{g.Size} {g.Station}")
    );
  }

  // Fails when CraftingGrid accepts an empty domain or station or a size below 1, or ClearDeclarations
  // keeps a grid.
  [Fact]
  public void CraftingGrid_names_a_domain_station_and_size_and_is_dropped_with_the_world() {
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.CraftingGrid("", 5, Station)
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.CraftingGrid("stub", 5, "")
    );
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      ExlibChecks.CraftingGrid("stub", 0, Station)
    );

    ExlibChecks.CraftingGrid("stub", 5, Station);
    ExlibChecks.ClearDeclarations();
    Assert.Single(ExlibChecks.Grids("stub"));
  }
}
