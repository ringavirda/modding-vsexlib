#if GAME_GE_1_22
using System.IO;
using System.Linq;
using ExpandedLib.Tests.Scenarios;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Which files load the grid recipes and craft sequences: those with a <c>craft</c> step, and those whose
/// <c>interact</c> steps hold an off-hand stack or click a control (setups.md, Loading).</summary>
[Collection("Setups")]
public class SetupRecipeLoadTests
{
  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Fixtures",
      name
    );

  private const string Valve =
    "{\"place\": {\"id\": \"valve\", \"code\": \"setupcontrol:valve\", \"at\": [0, 0, 0]}}";

  private static bool Loads(string interact)
  {
    using SetupScene scene = SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupcontrol\", \"setupcraft\"], \"steps\": ["
          + Valve
          + ", {\"interact\": {\"at\": {\"of\": \"valve\"}"
          + interact
          + "}}], \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [Fixture("SetupControl"), Fixture("SetupCraft")],
      null,
      w =>
      {
        SetupHoldTests.Register(w);
        w.RegisterClass("SetupControlBlock", typeof(SetupControlBlock))
          .RegisterClass("SetupControlEntity", typeof(SetupControlEntity))
          .RegisterBlockEntityFactory(
            "SetupControlEntity",
            () => new SetupControlEntity()
          )
          .RegisterClass(
            "CraftFixtureBenchBlock",
            typeof(CraftFixtureBenchBlock)
          )
          .RegisterClass("CraftFixtureBench", typeof(CraftFixtureBench))
          .RegisterBlockEntityFactory(
            "CraftFixtureBench",
            () => new CraftFixtureBench()
          )
          .RegisterClass("CraftFixtureChest", typeof(CraftFixtureChest))
          .RegisterBlockEntityFactory(
            "CraftFixtureChest",
            () => new CraftFixtureChest()
          );
      }
    );
    return scene.World.World.GridRecipes.Any(r =>
      r.Name?.ToString() == "setupcraft:frame"
    );
  }

  // Fails when an interact with an off-hand stack no longer asks for the recipes: a vice route's blank then finds no route.
  [Fact]
  public void An_interact_holding_an_off_hand_stack_loads_the_recipes() =>
    Assert.True(Loads(", \"offHand\": {\"stack\": \"setupcraft:mallet\"}"));

  // Fails when an interact on a control no longer asks for the recipes: the vice's seat click then finds no route.
  [Fact]
  public void An_interact_on_a_control_loads_the_recipes() =>
    Assert.True(Loads(", \"control\": \"handle\""));

  // Fails when every file loads the recipes: the trigger would no longer separate the files that need them.
  [Fact]
  public void A_plain_interact_loads_no_recipes() => Assert.False(Loads(""));
}
#endif
