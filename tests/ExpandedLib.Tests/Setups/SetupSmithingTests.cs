#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The hand-smithing stand-in: a <c>call</c> of <c>Smith</c> on an anvil holding a work item
/// finishes a recipe and pays its output (setups.md, Placers and stand-ins).</summary>
[Collection("Setups")]
public class SetupSmithingTests
{
  private const string Anvil =
    "{\"fill\": {\"code\": \"game:rock-granite\", \"from\": [0, 0, 0], \"to\": [3, 0, 3]}},"
    + "{\"place\": {\"id\": \"anvil\", \"code\": \"game:anvil-iron\", \"at\": [1, 1, 1]}},";

  private const string Heated =
    "{\"interact\": {\"at\": {\"of\": \"anvil\"}, \"with\": {\"stack\": \"game:ingot-iron\", \"temperature\": 1100}, \"keys\": [\"sneak\"], \"returns\": true}},";

  private static SetupScene Build(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupsmith\"], \"recipes\": true, \"load\": [\"game:workitem-iron\"], \"steps\": ["
          + steps
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupSmith"
        ),
      ],
      null,
      _ => { }
    );

  private static string Refused(string steps) =>
    Assert.Throws<InvalidOperationException>(() => Build(steps)).Message;

  // Fails when Smith does not set the voxels and run CheckIfFinished: the work item stays on the anvil and nothing spawns.
  [Fact]
  public void Smith_finishes_the_work_item_and_spawns_the_recipes_output()
  {
    using SetupScene scene = Build(
      Anvil
        + Heated
        + "{\"call\": {\"at\": {\"of\": \"anvil\"}, \"method\": \"Smith\", \"args\": [\"setupsmith:plug\"]}}"
    );

    var anvil = Assert.IsType<BlockEntityAnvil>(
      scene.World.GetBlockEntity(scene.PositionOf("anvil"))
    );
    Assert.Null(anvil.WorkItemStack);
    var (stack, _) = Assert.Single(scene.World.Spawns);
    Assert.Equal("setupsmith:plug", stack.Collectible.Code.ToString());
    Assert.Equal(3, stack.StackSize);
    Assert.Contains(
      scene.StandIns,
      s => s.StartsWith("stand-in Smith: ", StringComparison.Ordinal)
    );
  }

  // Fails when the recipe is not matched on its output: a code no recipe makes is accepted.
  [Fact]
  public void Smith_names_an_output_no_matching_recipe_makes()
  {
    string message = Refused(
      Anvil
        + Heated
        + "{\"call\": {\"at\": {\"of\": \"anvil\"}, \"method\": \"Smith\", \"args\": [\"setupsmith:nothing\"]}}"
    );

    Assert.Contains(
      "no smithing recipe the work item matches makes setupsmith:nothing",
      message
    );
  }

  // Fails when an empty anvil is let through: Smith then dereferences the missing work item.
  [Fact]
  public void Smith_on_an_anvil_with_no_work_item_fails()
  {
    string message = Refused(
      Anvil
        + "{\"call\": {\"at\": {\"of\": \"anvil\"}, \"method\": \"Smith\", \"args\": [\"setupsmith:plug\"]}}"
    );

    Assert.Contains("the anvil holds no work item", message);
  }

  // Fails when the argument count is unchecked.
  [Fact]
  public void Smith_takes_one_output_code()
  {
    string message = Refused(
      Anvil
        + Heated
        + "{\"call\": {\"at\": {\"of\": \"anvil\"}, \"method\": \"Smith\"}}"
    );

    Assert.Contains(
      "Smith takes the output code of one smithing recipe",
      message
    );
  }
}
#endif
