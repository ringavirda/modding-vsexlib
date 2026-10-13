#if GAME_GE_1_22
using System;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="CraftWalk"/> over a planted domain <c>walk</c> whose recipes chain from
/// vanilla codes.</summary>
public sealed class CraftWalkTests(ITestOutputHelper output) : IDisposable
{
  private readonly PlantedFiles _files = new();

  public void Dispose() => _files.Dispose();

  private static string Item(string code) =>
    $"{{ \"code\": \"{code}\", \"creativeinventory\": {{ \"general\": [\"*\"] }} }}";

  private static string Recipe(string input, string output) =>
    $$"""
      {
        "name": "walk:{{output}}",
        "ingredientPattern": "A",
        "width": 1,
        "height": 1,
        "ingredients": { "A": { "type": "item", "code": "{{input}}" } },
        "output": { "type": "item", "code": "walk:{{output}}" }
      }
      """;

  private TestWorld Planted(
    string[] items,
    string[] recipes,
    params string[] vanilla
  )
  {
    _files.Write(
      "modinfo.json",
      "{ \"type\": \"content\", \"modid\": \"walk\", \"name\": \"Walk\", \"version\": \"1.0.0\" }"
    );
    foreach (string item in items)
      _files.Write($"assets/walk/itemtypes/{item}.json", Item(item));
    _files.Write(
      "assets/walk/recipes/grid/walk.json",
      "[" + string.Join(",", recipes) + "]"
    );
    var world = new TestWorld();
    world.LoadAssets([_files.Root], null, vanilla);
    world.LoadRecipes();
    return world;
  }

  private CraftWalk.Result Walk(
    TestWorld world,
    string? kit = null,
    string? known = null
  )
  {
    CraftWalk.Result result = CraftWalk.Run(
      world,
      "walk",
      kit == null ? null : _files.Write("kit.json", kit),
      known == null ? null : _files.Write("known.json", known)
    );
    output.WriteLine(result.ToString());
    return result;
  }

  private const string Stick = "game:stick";

  // Fails when a recipe fires without its ingredients being reached, or the rounds stop early.
  [Fact]
  public void A_chain_from_a_gathered_code_passes()
  {
    using TestWorld world = Planted(
      ["a", "b", "c"],
      [Recipe(Stick, "a"), Recipe("walk:a", "b"), Recipe("walk:b", "c")],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    Assert.Equal(
      "craft walk walk: 3 kinds, 3 recipes fired (0 executed, 3 followed), 0 undeclared, 0 on trust, 0 failures",
      result.Summary
    );
  }

  // Fails when unreached kinds go unreported.
  [Fact]
  public void Dropping_the_middle_recipe_fails_naming_its_output()
  {
    using TestWorld world = Planted(
      ["a", "b", "c"],
      [Recipe(Stick, "a"), Recipe("walk:b", "c")],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    string finding = Assert.Single(result.Findings);
    Assert.StartsWith("walk:b | ", finding);
    Assert.Contains("no recipe makes it", finding);
    Assert.Contains("1 failures", result.Summary);
  }

  // Fails when the kit check is skipped.
  [Fact]
  public void A_game_input_in_neither_list_fails_naming_it_and_the_kit_clears_it()
  {
    using TestWorld world = Planted(
      ["a"],
      [Recipe("game:ingot-iron", "a")],
      "game:ingot-*"
    );

    CraftWalk.Result without = Walk(world);
    CraftWalk.Result with = Walk(
      world,
      kit: "{ \"game:ingot-iron\": { \"route\": \"smelted from ore\" } }"
    );

    string finding = Assert.Single(without.Findings);
    Assert.StartsWith("game:ingot-iron | asked for by walk:a", finding);
    Assert.Empty(with.Findings);
  }

  // Fails when a kit row that matches nothing loaded passes.
  [Fact]
  public void A_kit_row_matching_nothing_fails()
  {
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(
      world,
      kit: "{ \"game:ingot-iron\": \"smelted from ore\" }"
    );

    Assert.Equal(
      ["game:ingot-iron | kit row matches no loaded block or item"],
      result.Findings
    );
  }

  // Fails when stale rows pass.
  [Fact]
  public void A_known_row_whose_kind_passes_fails_as_stale()
  {
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(
      world,
      known: "{ \"walk:a\": { \"why\": \"unreached\", \"task\": \"CC9\" } }"
    );

    Assert.Equal(
      ["walk:a | known row matches no failure; remove it"],
      result.Findings
    );
  }

  // Fails when a known row stops accepting its failure.
  [Fact]
  public void A_known_row_accepts_its_failure()
  {
    using TestWorld world = Planted(["a", "b"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(
      world,
      known: "{ \"walk:b\": \"not built yet\" }"
    );

    Assert.Empty(result.Findings);
    Assert.Contains("0 failures", result.Summary);
  }

  private static string Sized(string output, int width, int height)
  {
    string[] rows =
    [
      .. Enumerable
        .Range(0, height)
        .Select(y => new string([
          .. Enumerable
            .Range(0, width)
            .Select(x => x == width / 2 && y == height / 2 ? 'A' : '_'),
        ])),
    ];
    return $$"""
      {
        "name": "walk:{{output}}",
        "ingredientPattern": "{{string.Join(",", rows)}}",
        "width": {{width}},
        "height": {{height}},
        "ingredients": { "A": { "type": "item", "code": "walk:a" } },
        "output": { "type": "item", "code": "walk:{{output}}" }
      }
      """;
  }

  // Fails when the class ignores width or height.
  [Fact]
  public void A_recipe_wider_or_taller_than_the_hand_grid_is_a_grid_station_recipe()
  {
    using TestWorld world = Planted(
      ["a", "small", "long", "tall", "member"],
      [
        Sized("small", 3, 3),
        Sized("long", 4, 1),
        Sized("tall", 1, 4),
        Sized("member", 5, 5),
      ]
    );

    CraftWalkClass Class(string output) =>
      CraftWalkEdges.ClassOf(
        world.World.GridRecipes.Single(r =>
          r.Name.ToString() == "walk:" + output
        )
      );

    Assert.Equal(CraftWalkClass.HandGrid, Class("small"));
    Assert.Equal(CraftWalkClass.GridStation, Class("long"));
    Assert.Equal(CraftWalkClass.GridStation, Class("tall"));
    Assert.Equal(CraftWalkClass.GridStation, Class("member"));
  }
}
#endif
