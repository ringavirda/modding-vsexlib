#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What the <c>pickup</c> step takes from the ground into the hand, what it draws from the pack and
/// when it refuses (setups.md, Steps).</summary>
[Collection("Setups")]
public class SetupPickUpTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Box =
    "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";
  private const string Giver =
    "{\"place\": {\"id\": \"giver\", \"code\": \"setuphold:givercold\", \"at\": [2, 0, 0]}}";
  private const string Tongs =
    ", \"offHand\": {\"stack\": \"setuphold:tongs\"}";
  private const string Pack =
    "{\"scope\": \"stacks\", \"gathered\": {}, \"ends\": {\"stacks\": {}}}";

  private static readonly (string, string)[] Mods =
  [
    ("exlib", "0.8.4"),
    ("setupbox", "1.0.0"),
    ("setupcraft", "1.0.0"),
    ("setuphold", "1.0.0"),
  ];

  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Fixtures",
      name
    );

  private static void Register(TestWorld w)
  {
    w.RegisterClass("SetupBox", typeof(BlockEntity))
      .RegisterBlockEntityFactory("SetupBox", () => new SetupBox())
      .RegisterClass("CraftFixtureBenchBlock", typeof(CraftFixtureBenchBlock))
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
    SetupHoldTests.Register(w);
  }

  private static SetupScene Build(SetupFile file) =>
    SetupScene.Build(
      file,
      [Fixture("SetupBox"), Fixture("SetupCraft"), Fixture("SetupHold")],
      null,
      Register
    );

  private static SetupScene Build(string pack, string steps) =>
    Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\", \"setupcraft\", \"setuphold\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}"
          + (pack == "" ? "" : ", \"pack\": " + pack)
          + "}",
        "t.json"
      )
    );

  private static string Refused(string pack, string steps) =>
    Assert.Throws<InvalidOperationException>(() => Build(pack, steps)).Message;

  private static string Spill(
    string key = "stockForm",
    string value = "flat",
    int count = 2,
    int dx = 0
  ) =>
    "{\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"SpillMarked\", \"args\": [\"setupcraft:plate\", "
    + count
    + ", \""
    + key
    + "\", \""
    + value
    + "\", "
    + dx
    + ", 1, 0]}}";

  private static string SpillHot() =>
    "{\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"SpillHot\", \"args\": [\"setupcraft:plate\", 1, 900, 0, 1, 0]}}";

  private static string PickUp(string extra = "", string at = "[0, 1, 0]") =>
    "{\"pickup\": {\"at\": " + at + extra + "}}";

  private static string HoldingText(string held) =>
    "the active hotbar slot holds "
    + held
    + "; \"pickup\" takes into an empty hand.";

  // Fails when the taken stack is rebuilt from its item and count, so the stockForm the spawn carried is lost.
  [Fact]
  public void The_picked_stack_keeps_its_attributes_through_an_interact()
  {
    using SetupScene scene = SetupScene.Build(
      SetupFile.Load(
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Files",
          "pickup.json"
        )
      ),
      [Fixture("SetupBox"), Fixture("SetupCraft")],
      null,
      Register
    );

    ItemStack held = scene.Player.ActiveSlot.Itemstack!;
    Assert.Equal(2, held.StackSize);
    Assert.Equal("flat", held.Attributes.GetString("stockForm"));
  }

  // Fails when the pickup does not draw the stack the pack was given, so the spawn is counted twice.
  [Fact]
  public void A_picked_spawn_is_drawn_once_and_given_back_by_the_interact()
  {
    using SetupScene scene = Build(
      Pack,
      "["
        + Box
        + ", "
        + Spill()
        + ", "
        + PickUp()
        + ", {\"interact\": {\"at\": {\"of\": \"box\"}}}]"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(2, pack.End["setupcraft:plate"]);
    Assert.Contains(("steps[2]", "setupcraft:plate", 2), pack.Draws);
  }

  // Fails when the pickup draws a spawn the ledger never gave, so a pack holding no plate refuses it.
  [Fact]
  public void A_spawn_the_ledger_did_not_give_is_picked_without_a_draw()
  {
    using SetupScene scene = Build(
      Pack,
      "[" + Box + ", " + Spill(dx: 3) + ", " + PickUp(at: "[3, 0, 0]") + "]"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(2, scene.Player.ActiveSlot.Itemstack!.StackSize);
    Assert.DoesNotContain(pack.Draws, d => d.Where == "steps[2]");
  }

  // Fails when Pick answers an empty match with a candidate, or the step does not refuse it.
  [Fact]
  public void A_pickup_where_nothing_lies_is_refused()
  {
    string message = Refused("", "[" + Box + ", " + PickUp() + "]");

    Assert.Equal(
      "t.json: steps[1]: no item entity lies within one cell of (0, 1, 0).",
      message
    );
  }

  // Fails when Pick widens its cell to two, so a spawn two cells off is taken.
  [Fact]
  public void A_spawn_two_cells_off_is_not_within_one_cell()
  {
    string message = Refused(
      "",
      "[" + Box + ", " + Spill(dx: 2) + ", " + PickUp() + "]"
    );

    Assert.Equal(
      "t.json: steps[2]: no item entity lies within one cell of (0, 1, 0).",
      message
    );
  }

  // Fails when a break step's drops stay candidates, so the pickup takes the broken block's drop.
  [Fact]
  public void A_broken_blocks_drops_are_not_picked()
  {
    string message = Refused(
      "",
      "[{\"place\": {\"code\": \"setupbox:slab\", \"at\": [0, 1, 0]}}, "
        + "{\"break\": {\"at\": [0, 1, 0], \"by\": \"world\"}}, "
        + PickUp()
        + "]"
    );

    Assert.Equal(
      "t.json: steps[2]: no item entity lies within one cell of (0, 1, 0).",
      message
    );
  }

  // Fails when the code filter is skipped, so a plate is taken for a box.
  [Fact]
  public void A_pickup_whose_code_matches_nothing_there_is_refused()
  {
    string message = Refused(
      "",
      "["
        + Box
        + ", "
        + Spill()
        + ", "
        + PickUp(", \"code\": \"setupbox:box\"")
        + "]"
    );

    Assert.Equal(
      "t.json: steps[2]: no item entity within one cell of (0, 1, 0) is setupbox:box; there: setupcraft:plate x2.",
      message
    );
  }

  // Fails when the hand refusal is skipped, so a held plate is overwritten.
  [Fact]
  public void A_pickup_into_a_full_hand_is_refused()
  {
    string message = Refused(
      "{\"scope\": \"stacks\", \"gathered\": {\"setupcraft:plate\": 1}}",
      "["
        + Box
        + ", {\"interact\": {\"at\": {\"of\": \"box\"}, \"with\": {\"stack\": \"setupcraft:plate\"}, \"keep\": true}}, "
        + Spill()
        + ", "
        + PickUp()
        + "]"
    );

    Assert.Equal(
      "t.json: steps[3]: " + HoldingText("setupcraft:plate x1"),
      message
    );
  }

  // Fails when a picked spawn is still a candidate, so the second pickup takes it again.
  [Fact]
  public void A_spawn_is_picked_once()
  {
    string message = Refused(
      "",
      "["
        + Box
        + ", "
        + Spill()
        + ", "
        + PickUp()
        + ", {\"select\": {\"slot\": 1}}, "
        + PickUp()
        + "]"
    );

    Assert.Equal(
      "t.json: steps[4]: no item entity lies within one cell of (0, 1, 0).",
      message
    );
  }

  // Fails when the candidates are walked from the last spawn, so the b plate is taken.
  [Fact]
  public void The_earliest_spawn_is_picked()
  {
    using SetupScene scene = Build(
      "",
      "["
        + Box
        + ", "
        + Spill(value: "a")
        + ", "
        + Spill(value: "b")
        + ", "
        + PickUp()
        + ", {\"expect\": {\"slot\": 0, \"code\": \"setupcraft:plate\", \"attribute\": {\"key\": \"stockForm\", \"equals\": \"a\"}}}]"
    );

    Assert.Equal(
      "a",
      scene.Player.ActiveSlot.Itemstack!.Attributes.GetString("stockForm")
    );
  }

  private static ItemStack Plate(SetupScene scene, int count) =>
    new(
      scene.World.World.GetItem(new AssetLocation("setupcraft:plate")),
      count
    );

  // Fails when Pick does not skip a stack of size 0, so the empty one is chosen first.
  [Fact]
  public void An_empty_spawn_is_never_picked()
  {
    using SetupScene scene = Build("", "[" + Box + "]");
    var at = new Vec3d(0.5, 1.5, 0.5);

    int? choice = SetupPickUp.Pick(
      [(Plate(scene, 0), at), (Plate(scene, 2), at)],
      new BlockPos(0, 1, 0),
      null,
      "(0, 1, 0)",
      out _
    );

    Assert.Equal(1, choice);
  }

  // Fails when the heatResistant clause of HeatRefusal is removed, so tongs in the off hand refuse the pickup.
  [Fact]
  public void A_hot_spawn_taken_with_tongs_is_laid_hot_by_the_next_click()
  {
    using SetupScene scene = Build(
      "",
      "["
        + Box
        + ", "
        + Giver
        + ", "
        + SpillHot()
        + ", "
        + PickUp(Tongs)
        + ", {\"interact\": {\"at\": {\"of\": \"giver\"}"
        + Tongs
        + "}}]"
    );

    var giver = Assert.IsType<SetupHoldGiver>(
      scene.World.GetBlock(scene.PositionOf("giver"))
    );
    Assert.Equal(1, giver.Laid);
    Assert.True(giver.LaidTemperature > 850f);
    Assert.Null(scene.Player.ActiveSlot.Itemstack);
  }

  // Fails when the HeatRefusal call is skipped in the step, so a hot plate is taken bare-handed.
  [Fact]
  public void A_hot_spawn_without_tongs_is_refused()
  {
    string message = Refused(
      "",
      "[" + Box + ", " + SpillHot() + ", " + PickUp() + "]"
    );

    Assert.Matches(
      "^t\\.json: steps\\[2\\]: setupcraft:plate x1 is 9\\d\\d C, too hot to hold; \"pickup\" needs a heatResistant offHand, such as tongs\\.$",
      message
    );
  }

  // Fails when the allowHotCrafting clause is removed (the first answer), or when every stack counts as hot (the second).
  [Fact]
  public void Heat_refuses_only_a_hot_plain_stack()
  {
    using SetupScene scene = Build("", "[" + Box + "]");
    IWorldAccessor world = scene.World.World;
    var crafted = new Item
    {
      Code = new AssetLocation("t:crafted"),
      Attributes = new JsonObject(JToken.Parse("{\"allowHotCrafting\": true}")),
    };
    var hot = new ItemStack(crafted, 1);
    crafted.SetTemperature(world, hot, 900f);
    ItemStack cold = Plate(scene, 1);
    cold.Collectible.SetTemperature(world, cold, 20f);

    Assert.Null(SetupPickUp.HeatRefusal(world, hot, null));
    Assert.Null(SetupPickUp.HeatRefusal(world, cold, null));
  }

  // Fails when a pickup is allowed in a during, which the live runner gives its spawns to only at settle.
  [Fact]
  public void A_pickup_in_a_during_is_refused_at_load()
  {
    var e = Assert.Throws<InvalidOperationException>(() =>
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": ["
          + Box
          + "], \"seconds\": 10, \"record\": {}, \"during\": [{\"at\": 2, \"steps\": ["
          + PickUp()
          + "]}]}",
        "t.json"
      )
    );

    Assert.Contains("\"pickup\" is not a step in a \"during\"", e.Message);
  }
}
#endif
