#if GAME_GE_1_22
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The game's own grid recipes in <see cref="TestWorld.LoadRecipes"/>.</summary>
public class RecipeLoadingTests
{
  private static readonly string[] Plated =
  [
    "game:metalplate-*",
    "game:metalnailsandstrips-*",
    "game:hammer-*",
  ];

  private static TestWorld Loaded(params string[] vanilla)
  {
    var world = new TestWorld();
    world.LoadAssets(
      [Path.Combine(RepoPaths.Root, "samples", "PlatedPipes")],
      null,
      [.. Plated, .. vanilla]
    );
    return world;
  }

  private static GridRecipe[] ChestRecipes(TestWorld world) =>
    [
      .. world.World.GridRecipes.Where(r =>
        r.Output.Code.ToString() == "game:chest-east"
      ),
    ];

  // Fails when LoadRecipes skips the game's recipes/grid folder.
  [Fact]
  public void LoadRecipes_keeps_the_vanilla_chest_as_a_3_x_3_grid_recipe()
  {
    using TestWorld world = Loaded("game:plank-*", "game:chest-*");

    world.LoadRecipes();

    GridRecipe[] chests = ChestRecipes(world);
    Assert.NotEmpty(chests);
    Assert.All(
      chests,
      r =>
      {
        Assert.Equal(3, r.Width);
        Assert.Equal(3, r.Height);
      }
    );
  }

  // Fails when the game's recipes log through the world's recorder instead of being discarded.
  [Fact]
  public void A_vanilla_recipe_with_unloaded_ingredients_fails_without_logging()
  {
    using TestWorld world = Loaded("game:plank-*");

    (_, int failed) = world.LoadRecipes();

    Assert.True(failed > 0);
    Assert.Empty(ChestRecipes(world));
    Assert.DoesNotContain(
      world.Log.Entries,
      e => e.Message.Contains("game:chest", System.StringComparison.Ordinal)
    );
  }

  // Fails when HandGrid.Orders reads the output stack of a loaded recipe whose output did not resolve.
  [Fact]
  public void The_hand_grid_orders_only_recipes_whose_output_resolved()
  {
    using TestWorld world = Loaded("game:plank-*", "game:chest-*");
    world.LoadRecipes();

    IReadOnlyList<CraftOrder> orders = HandGrid.Orders(world.World);

    Assert.NotEmpty(orders);
    Assert.All(orders, o => Assert.NotNull(o.Product));
  }
}
#endif
