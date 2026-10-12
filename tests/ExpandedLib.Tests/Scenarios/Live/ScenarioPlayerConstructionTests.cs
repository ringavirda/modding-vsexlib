using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
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
}
