using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>What a live case draws from and gives to its pack.</summary>
public class PackLedgerTests {
  private static string Place(string beside = "") =>
    """{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } """
    + beside
    + "}";

  private static LiveFixture Started(
    string steps,
    string pack,
    out ScenarioCase scenario
  ) {
    scenario = LiveFixture.Case(steps, "{ pack: " + pack + " }");
    var fixture = new LiveFixture();
    fixture.Context.Pack = SetupPack.Start(scenario.Model, _ => null);
    return fixture;
  }

  private static string Failure(LiveFixture fixture, ScenarioCase scenario) =>
    Assert.Throws<StepFailure>(() => fixture.Run(scenario.Model.Steps)).Message;

  /// <summary>Fails when a placed block is not drawn from the pack, so a case short of the block
  /// places it.</summary>
  [Fact]
  public void A_block_placed_short_of_the_pack_fails_at_its_step() {
    LiveFixture fixture = Started(
      "[" + Place() + "]",
      """{ scope: "all", gathered: { "game:flint": 1 } }""",
      out ScenarioCase scenario
    );

    Assert.Equal(
      "the pack holds 0 of game:stone; the step takes 1.",
      Failure(fixture, scenario)
    );
  }

  /// <summary>Fails when the <c>draws</c> of a step are not taken before it runs, so a step short of
  /// them runs.</summary>
  [Fact]
  public void A_step_whose_draws_exceed_the_pack_fails_at_its_step() {
    LiveFixture fixture = Started(
      "[" + Place(""", "draws": { "game:stick": 2 } """) + "]",
      """{ scope: "stacks", gathered: { "game:stick": 1 } }""",
      out ScenarioCase scenario
    );

    Assert.Equal(
      "the pack holds 1 of game:stick; the step takes 2.",
      Failure(fixture, scenario)
    );
  }

  /// <summary>Fails when the <c>gives</c> of a step are not handed to the pack, so the stack it
  /// declares is missing at the end.</summary>
  [Fact]
  public void The_gives_of_a_step_reach_the_pack_by_the_end_of_the_case() {
    LiveFixture fixture = Started(
      "[" + Place(""", "gives": { "game:stick": 3 } """) + "]",
      """{ scope: "stacks", gathered: {}, ends: { stacks: { "game:stick": 3 } } }""",
      out ScenarioCase scenario
    );

    fixture.Run(scenario.Model.Steps);

    PackLedger.Finish(fixture.Context);
  }

  /// <summary>Fails when an ingredient the pack holds in one variant is laid as the first loaded
  /// variant, so a craft draws a plank the pack does not have.</summary>
  [Fact]
  public void A_wildcard_ingredient_is_laid_as_the_variant_the_pack_holds() {
    LiveFixture fixture = Started(
      "[" + Place() + "]",
      """{ scope: "all", gathered: { "game:plank-pine": 2 } }""",
      out _
    );
    fixture.Test.RegisterItem("game:plank-oak");
    fixture.Test.RegisterItem("game:plank-pine");
    var ingredient = new CraftingRecipeIngredient {
      Code = new AssetLocation("game:plank-*"),
      Type = EnumItemClass.Item,
      Quantity = 2,
    };
    ingredient.Resolve(fixture.Test.World, "test");
    var input = new CraftInput(0, "plank", ingredient);

    ItemStack laid = PackLedger.Laid(fixture.Context, input, null);

    Assert.Equal("game:plank-pine", laid.Collectible.Code.ToString());
    Assert.Equal(2, laid.StackSize);
  }

  /// <summary>Fails when the margin around the box is clipped on its high sides, so an item that
  /// lands between one and two cells above, east or south of it is not given.</summary>
  [Theory]
  [InlineData(101.7, 50.5, 100.5, 1)]
  [InlineData(100.5, 51.7, 100.5, 1)]
  [InlineData(100.5, 50.5, 101.7, 1)]
  [InlineData(99.2, 49.2, 99.2, 1)]
  [InlineData(102.2, 50.5, 100.5, 0)]
  [InlineData(100.5, 50.5, 98.8, 0)]
  public void An_item_within_one_cell_of_the_box_is_given(
    double x,
    double y,
    double z,
    int given
  ) {
    LiveFixture fixture = Started(
      "[" + Place() + "]",
      """{ scope: "stacks", gathered: {}, ends: { stacks: {} } }""",
      out ScenarioCase scenario
    );
    Item flint = fixture.Test.RegisterItem("game:flint");
    var spawned = new EntityItem {
      EntityId = 7,
      Itemstack = new ItemStack(flint, 1),
    };
    spawned.Pos.SetPos(x, y, z);
    fixture
      .Test.World.GetEntitiesInsideCuboid(
        Arg.Any<BlockPos>(),
        Arg.Any<BlockPos>(),
        Arg.Any<ActionConsumable<Entity>>()
      )
      .Returns(call => {
        var min = call.ArgAt<BlockPos>(0);
        var max = call.ArgAt<BlockPos>(1);
        var test = call.ArgAt<ActionConsumable<Entity>>(2);
        return new Entity[] { spawned }
          .Where(e =>
            e.Pos.X >= min.X
            && e.Pos.X <= max.X
            && e.Pos.Y >= min.Y
            && e.Pos.Y <= max.Y
            && e.Pos.Z >= min.Z
            && e.Pos.Z <= max.Z
            && test(e)
          )
          .ToArray();
      });

    fixture.Run(scenario.Model.Steps);

    Assert.Equal(given, fixture.Context.Pack!.Held("game:flint"));
  }
}
