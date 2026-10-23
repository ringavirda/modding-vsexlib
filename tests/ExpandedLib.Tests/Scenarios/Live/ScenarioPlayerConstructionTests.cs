using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The construction walk of <c>complete</c>: the stage it stops at, the variants it supplies and
/// what it needs to run (scenarios.md, <c>place</c> and <c>complete</c>).</summary>
public class ScenarioPlayerConstructionTests {
  private sealed class StubBlockEntity : BlockEntity;

  private static ExConstructionIngredient Ingredient(
    string code,
    int quantity,
    string? stores
  ) =>
    new() {
      Code = new AssetLocation(code),
      Type = EnumItemClass.Item,
      Quantity = quantity,
      StoreWildCard = stores,
    };

  private static (TestWorld World, ExRightClickConstruction Rcc) Stages(
    params ExConstructionIngredient[] stage
  ) {
    var world = new TestWorld();
    foreach (
      string code in new[]
      {
        "ingot-iron",
        "ingot-steel",
        "plate-steel",
        "plate-iron",
      }
    )
      world.RegisterItem("game:" + code).VariantStrict["metal"] = code[
        (code.IndexOf('-') + 1)..
      ];
    var rcc = new ExRightClickConstruction {
      Stages =
      [
        new ExConstructionStage(),
        new ExConstructionStage { RequireStacks = stage },
      ],
    };
    return (world, rcc);
  }

  private static string[] Codes(IEnumerable<ItemStack> stacks) =>
    [.. stacks.Select(s => $"{s.Collectible.Code} x {s.StackSize}")];

  /// <summary>Fails when "all" or an absent stage stops one past the last (S42), or a stage past the
  /// last is accepted (S43).</summary>
  [Fact]
  public void A_stage_is_an_index_of_the_construction_or_all() {
    Assert.Equal(3, ConstructionWalk.Target(null, 4));
    Assert.Equal(3, ConstructionWalk.Target(JValue.CreateString("all"), 4));
    Assert.Equal(3, ConstructionWalk.Target(new JValue(true), 4));
    Assert.Equal(2, ConstructionWalk.Target(new JValue(2), 4));
    var e = Assert.Throws<StepFailure>(() =>
      ConstructionWalk.Target(new JValue(4), 4)
    );
    Assert.Equal(
      "\"to\" 4 is not a stage of a construction of 4 stage(s), 0 to 3.",
      e.Message
    );
  }

  /// <summary>Fails when a negative stage is accepted (S44).</summary>
  [Fact]
  public void A_negative_stage_fails_the_step() {
    Assert.Throws<StepFailure>(() =>
      ConstructionWalk.Target(new JValue(-1), 4)
    );
  }

  /// <summary>Fails when a materials value that is not a name passes (S45).</summary>
  [Fact]
  public void Materials_map_each_key_to_a_variant_name() {
    Dictionary<string, string> read = ConstructionWalk.Materials(
      JObject.Parse("""{ "metal": "iron", "wood": "oak" }""")
    );
    Assert.Equal("iron", read["metal"]);
    Assert.Equal("oak", read["wood"]);
    Assert.Empty(ConstructionWalk.Materials(null));
    var e = Assert.Throws<StepFailure>(() =>
      ConstructionWalk.Materials(JObject.Parse("""{ "metal": 3 }"""))
    );
    Assert.Equal(
      "\"materials\" maps each wildcard key to a variant name.",
      e.Message
    );
  }

  /// <summary>Fails when the hold is shorter than the step's hits at the default stroke, or a stage with
  /// no tool has a step to hold.</summary>
  [Fact]
  public void A_hold_runs_every_hit_of_the_step_and_a_plain_stage_has_none() {
    Assert.Equal(
      (3 * 1.05) + 0.1,
      ConstructionWalk.HoldSeconds(
        new ExConstructionStage { Tool = "game:hammer-*", Hits = 3 }.ToolSteps[
          0
        ],
        null
      ),
      3
    );
    Assert.Empty(new ExConstructionStage().ToolSteps);
  }

  /// <summary>Fails when a stage with no tool leaves the active slot empty, so a block that reads the
  /// hand for its ingredient (the bench's vice) is clicked with nothing in it, or when a tool is not
  /// kept in the active slot.</summary>
  [Fact]
  public void A_stage_without_a_tool_puts_its_first_material_in_the_hand() {
    var vice = new ItemStack(
      new Item { Code = new AssetLocation("game:vice") }
    );
    var plank = new ItemStack(
      new Item { Code = new AssetLocation("game:plank") }
    );
    var hammer = new ItemStack(
      new Item { Code = new AssetLocation("game:hammer") }
    );
    ItemStack?[] bare = ScenarioPlayer.Lay(4, 2, [vice, plank], null);
    Assert.Same(vice, bare[2]);
    Assert.Same(plank, bare[0]);
    ItemStack?[] held = ScenarioPlayer.Lay(4, 2, [vice, plank], hammer);
    Assert.Same(hammer, held[2]);
    Assert.Same(vice, held[0]);
    Assert.Same(plank, held[1]);
  }

  /// <summary>Fails when a variant other than the wanted one is allowed (S46), or a key the materials
  /// leave out is refused (S47).</summary>
  [Fact]
  public void A_variant_is_allowed_when_its_key_is_unwanted_or_wanted_in_it() {
    var wanted = new Dictionary<string, string> { ["metal"] = "iron" };
    Assert.True(ConstructionWalk.Allows(null, null, wanted));
    Assert.True(ConstructionWalk.Allows("wood", "oak", wanted));
    Assert.True(ConstructionWalk.Allows("metal", "iron", wanted));
    Assert.False(ConstructionWalk.Allows("metal", "steel", wanted));
  }

  /// <summary>Fails when the materials do not pick the variant supplied (S48).</summary>
  [Fact]
  public void The_supply_takes_the_variant_the_materials_name() {
    (TestWorld world, ExRightClickConstruction rcc) = Stages(
      Ingredient("game:ingot-*", 2, "metal")
    );
    Assert.Equal(
      ["game:ingot-iron x 2"],
      Codes(
        ConstructionWalk.Supply(
          world.World,
          rcc,
          1,
          new Dictionary<string, string>()
        )
      )
    );
    Assert.Equal(
      ["game:ingot-steel x 2"],
      Codes(
        ConstructionWalk.Supply(
          world.World,
          rcc,
          1,
          new Dictionary<string, string> { ["metal"] = "steel" }
        )
      )
    );
  }

  /// <summary>Fails when a second ingredient storing the same key is supplied in another variant
  /// than the first took (S49).</summary>
  [Fact]
  public void Ingredients_of_one_stage_storing_one_key_are_supplied_in_one_variant() {
    (TestWorld world, ExRightClickConstruction rcc) = Stages(
      Ingredient("game:plate-*", 1, "metal"),
      Ingredient("game:ingot-*", 1, "metal")
    );
    Assert.Equal(
      ["game:plate-steel x 1", "game:ingot-steel x 1"],
      Codes(
        ConstructionWalk.Supply(
          world.World,
          rcc,
          1,
          new Dictionary<string, string>()
        )
      )
    );
  }

  /// <summary>Fails when an ingredient nothing supplies is skipped (S50), or its failure drops the
  /// code asked for (S51).</summary>
  [Fact]
  public void An_ingredient_no_collectible_supplies_fails_naming_it() {
    (TestWorld world, ExRightClickConstruction rcc) = Stages(
      Ingredient("game:ingot-*", 1, "metal")
    );
    var e = Assert.Throws<StepFailure>(() =>
      ConstructionWalk.Supply(
        world.World,
        rcc,
        1,
        new Dictionary<string, string> { ["metal"] = "gold" }
      )
    );
    Assert.Equal(
      "stage 1 asks for game:ingot-*, which no registered item supplies in metal gold.",
      e.Message
    );
  }

  /// <summary>Fails when a construction is walked without the scenario player (S52).</summary>
  [Fact]
  public void A_construction_walk_without_the_scenario_player_fails_the_step() {
    var fixture = new LiveFixture();
    var block = TestBlocks.Configure(new Block(), "stub:rccblock", 4999);
    var entity = new StubBlockEntity {
      Block = block,
      Pos = new BlockPos(0, 0, 0, 0),
    };
    entity.Initialize(fixture.Test.Api);
    var behavior = new ExRightClickConstructable(entity);
    behavior.Initialize(
      fixture.Test.Api,
      new JsonObject(
        new JObject {
          ["stages"] = new JArray { new JObject(), new JObject() },
        }
      )
    );
    var e = Assert.Throws<StepFailure>(() =>
      ConstructionWalk
        .Walk(
          fixture.Context,
          new BlockPos(0, 0, 0),
          behavior,
          null,
          null,
          new StepReport()
        )
        .ToList()
    );
    Assert.Equal(
      "\"complete\" of a construction needs the scenario player.",
      e.Message
    );
  }

  /// <summary>Fails when the walk of a two-step stage pays the first step only, so the second tool is
  /// never held and the stage is left unbuilt.</summary>
  [Fact]
  public void A_stage_with_two_tool_steps_is_paid_one_click_per_step_each_with_its_own_tool() {
    var fixture = new LiveFixture();
    TestWorld world = fixture.Test;
    Item hammer = world.RegisterItem("game:hammer-iron");
    Item tongs = world.RegisterItem("test:tongs-iron");
    Item rod = world.RegisterItem("test:rod");
    var entity = new StubBlockEntity {
      Block = TestBlocks.Configure(new Block(), "stub:twosteps", 5102),
      Pos = new BlockPos(0, 0, 0, 0),
    };
    entity.Initialize(world.Api);
    var behavior = new ExRightClickConstructable(entity);
    behavior.Initialize(
      world.Api,
      new JsonObject(
        new Definitions.ConstructionStages()
          .Stage(_ => { })
          .Stage(t =>
            t.Tool("game:hammer-*", 2)
              .Tool("test:tongs-*", 3)
              .Require("test:rod", 1)
          )
          .Build()
      )
    );
    TestPlayer payer = world.Player();
    payer.GameMode = EnumGameMode.Survival;
    payer.Entity.AnimManager = Substitute.For<IAnimationManager>();
    var held = new List<string>();
    var seconds = new List<double>();

    ConstructionWalk.StageClick Click(
      List<ItemStack> supply,
      ItemStack? tool,
      double used
    ) {
      held.Add(tool!.Collectible.Code.ToString());
      seconds.Add(used);
      payer.Hold(tool);
      payer.Hotbar[0].Itemstack = supply[0];
      EnumHandling handling = EnumHandling.PassThrough;
      var selection = new BlockSelection {
        Position = new BlockPos(0, 0, 0, 0),
      };
      bool answered = behavior.OnBlockInteractStart(
        world.World,
        payer.Player,
        selection,
        ref handling
      );
      behavior.OnBlockInteractStep(
        (float)used,
        world.World,
        payer.Player,
        selection,
        ref handling
      );
      behavior.OnBlockInteractStop(
        (float)used,
        world.World,
        payer.Player,
        selection,
        ref handling
      );
      return new ConstructionWalk.StageClick(
        answered,
        "",
        payer.ActiveSlot.Itemstack
      );
    }

    ConstructionWalk.StagePayment paid = ConstructionWalk.Pay(
      fixture.Context,
      behavior.Construction,
      1,
      "stage 1",
      [new ItemStack(rod)],
      Click
    );

    Assert.Equal(["game:hammer-iron", "test:tongs-iron"], held);
    Assert.Equal(1, behavior.Construction.CurrentCompletedStage);
    Assert.Equal(tongs, paid.Tool!.Collectible);
    Assert.Equal(
      [
        ConstructionWalk.HoldSeconds(
          behavior.Construction.Stages[1].ToolSteps[0],
          new ItemStack(hammer)
        ),
        ConstructionWalk.HoldSeconds(
          behavior.Construction.Stages[1].ToolSteps[1],
          new ItemStack(tongs)
        ),
      ],
      seconds
    );
  }
}
