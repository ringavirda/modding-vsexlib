// PlatedPipes builds only for the current game version.
#if GAME_GE_1_22
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The game's recipe loader in the harness: a mod's recipes load through
/// <see cref="TestWorld.LoadRecipes"/>, resolve, and match as the game matches them.</summary>
public class HarnessRecipesTests
{
  private const string Straight = "platedpipes:pipe-plated-straight-ns";

  private static readonly string[] Vanilla =
  [
    "game:metalplate-*",
    "game:metalnailsandstrips-*",
    "game:hammer-*",
  ];

  private static TestWorld Loaded()
  {
    var world = new TestWorld();
    world.LoadAssets(
      [Path.Combine(RepoPaths.Root, "samples", "PlatedPipes")],
      null,
      Vanilla
    );
    foreach (Item item in world.World.Items.Where(i => i?.Code != null))
      item.OnLoadedNative(world.Api);
    return world;
  }

  private static GridRecipe[] StraightRecipes(TestWorld world) =>
    [
      .. world.World.GridRecipes.Where(r =>
        r.Output.Code.ToString() == Straight
      ),
    ];

  private static bool StraightMatches(TestWorld world, TestPlayer player) =>
    StraightRecipes(world)
      .Any(r => r.Matches(player.Player, world.World, Bill(world), 3));

  private static ItemSlot[] Bill(TestWorld world) =>
    [
      .. new[]
      {
        "hammer-iron",
        "metalplate-iron",
        "metalnailsandstrips-iron",
      }.Select(code =>
        (ItemSlot)
          new DummySlot(
            new ItemStack(world.World.GetItem(new AssetLocation("game", code)))
          )
      ),
      .. Enumerable.Range(0, 6).Select(_ => (ItemSlot)new DummySlot()),
    ];

  // Fails when the loader's delegate keeps nothing.
  [Fact]
  public void LoadRecipes_resolves_the_plated_straight()
  {
    using TestWorld world = Loaded();

    (int loaded, int failed) = world.LoadRecipes();

    Assert.Equal(0, failed);
    Assert.True(loaded >= 4, $"loaded {loaded}");
    GridRecipe[] recipes = StraightRecipes(world);
    Assert.NotEmpty(recipes);
    Assert.All(recipes, r => Assert.Equal(2, r.Output.Quantity));
    Assert.All(recipes, r => Assert.Equal(3, r.ResolvedIngredients.Length));
  }

  // Fails when FastSearchRecipesByIngredient is left unwired on the server world.
  [Fact]
  public void A_resolved_ingredient_reads_its_stack_through_the_fast_search_index()
  {
    using TestWorld world = Loaded();
    world.LoadRecipes();

    CraftingRecipeIngredient plate = StraightRecipes(world)[0]
      .ResolvedIngredients.Single(i => i.Code.Path.StartsWith("metalplate-"));

    Assert.StartsWith(
      "game:metalplate-",
      plate.ResolvedItemStack.Collectible.Code.ToString()
    );
  }

  // Fails when Api.Assets stays the substitute after a LoadAssets load.
  [Fact]
  public void LoadRecipes_finds_the_recipe_files_of_a_LoadAssets_load()
  {
    using TestWorld world = Loaded();

    Assert.NotEmpty(
      world.Api.Assets.GetMany<JToken>(world.Log, "recipes/grid")
    );
  }

  // Fails when TestPlayer.Create leaves the entity's Api unset.
  [Fact]
  public void A_test_player_matches_a_loaded_recipe()
  {
    using TestWorld world = Loaded();
    world.LoadRecipes();
    TestPlayer player = world.Player();

    Assert.True(StraightMatches(world, player));
  }

  // Fails when TriggerMatchesRecipe answers true whatever the handlers say.
  [Fact]
  public void A_refusing_handler_refuses_in_the_harness()
  {
    using TestWorld world = Loaded();
    world.LoadRecipes();
    TestPlayer player = world.Player();
    world.Api.Event.MatchesRecipe += (_, _, _) => false;

    Assert.False(StraightMatches(world, player));
  }
}
#endif
