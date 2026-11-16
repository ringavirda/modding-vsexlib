#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The live <c>pickup</c> step: the entity it chooses, the stack it takes and what it refuses
/// (setups.md, The live runner).</summary>
public class PickUpStepTests
{
  private static readonly BlockPos Cell = new(100, 50, 100);

  private static EntityItem Spawned(
    long id,
    ItemStack stack,
    double x,
    bool alive = true
  )
  {
    var item = new EntityItem
    {
      EntityId = id,
      Itemstack = stack,
      Alive = alive,
    };
    item.Pos.SetPos(x, 50.5, 100.5);
    return item;
  }

  private static void Lie(LiveFixture fixture, params EntityItem[] items) =>
    fixture
      .Test.World.GetEntitiesInsideCuboid(
        Arg.Any<BlockPos>(),
        Arg.Any<BlockPos>(),
        Arg.Any<ActionConsumable<Entity>>()
      )
      .Returns(call =>
      {
        var min = call.ArgAt<BlockPos>(0);
        var max = call.ArgAt<BlockPos>(1);
        var test = call.ArgAt<ActionConsumable<Entity>>(2);
        return items
          .Where(e =>
            e.Pos.X >= min.X
            && e.Pos.X <= max.X
            && e.Pos.Y >= min.Y
            && e.Pos.Y <= max.Y
            && e.Pos.Z >= min.Z
            && e.Pos.Z <= max.Z
            && test(e)
          )
          .Cast<Entity>()
          .ToArray();
      });

  private static LiveFixture Packed(int flint)
  {
    ScenarioCase scenario = LiveFixture.Case(
      "[]",
      "{ pack: { scope: \"stacks\", gathered: { \"game:flint\": "
        + flint
        + " }, ends: { stacks: {} } } }"
    );
    var fixture = new LiveFixture();
    fixture.Context.Pack = SetupPack.Start(scenario.Model, _ => null);
    return fixture;
  }

  private static EntityItem MarkedFlint(LiveFixture fixture)
  {
    Item flint = fixture.Test.RegisterItem("game:flint");
    var stack = new ItemStack(flint, 2);
    stack.Attributes.SetString("stockForm", "flat");
    return Spawned(7, stack, 100.5);
  }

  /// <summary>Fails when the live runner refuses <c>pickup</c> again, with or without a <c>code</c> or an
  /// <c>offHand</c>.</summary>
  [Fact]
  public void A_pickup_is_accepted_by_the_live_runner()
  {
    LiveFixture.Case("""[{ "pickup": { "at": [0, 1, 0] } }]""");
    LiveFixture.Case(
      """[{ "pickup": { "at": [0, 1, 0], "code": "game:flint" } }]"""
    );
    LiveFixture.Case(
      """[{ "pickup": { "at": [0, 1, 0], "offHand": { "stack": "game:tongs" } } }]"""
    );
  }

  /// <summary>Fails when the choice is not the lowest entity id (the answer is 9 without the order), or
  /// when a dead entity is a candidate (the answer is 1 without the filter).</summary>
  [Fact]
  public void The_lowest_live_entity_id_within_one_cell_is_chosen()
  {
    var fixture = new LiveFixture();
    Item flint = fixture.Test.RegisterItem("game:flint");
    ItemStack One() => new(flint, 1);
    Lie(
      fixture,
      Spawned(9, One(), 100.5),
      Spawned(4, One(), 101.5),
      Spawned(2, One(), 102.5),
      Spawned(1, One(), 100.5, alive: false)
    );

    List<EntityItem> near = PickUpStep.Around(fixture.Context, Cell);
    int? chosen = SetupPickUp.Pick(
      [.. near.Select(e => (e.Itemstack, e.Pos.XYZ))],
      Cell,
      null,
      "plot (0, 0, 0)",
      out string? problem
    );

    Assert.Null(problem);
    Assert.Equal(4, near[chosen!.Value].EntityId);
  }

  /// <summary>Fails when the taken stack is not a clone with its attribute tree (without the tree
  /// <c>stockForm</c> is gone), when the pack is drawn for a spawn it was never given (without the
  /// <c>SpawnsSeen</c> check the second pack falls to 0), when a given spawn is not drawn (the first pack stays
  /// at 2), or when the entity is not recorded as seen (the second set lacks 7).</summary>
  [Fact]
  public void A_taken_stack_keeps_its_attributes_and_is_drawn_only_when_given()
  {
    LiveFixture given = Packed(2);
    EntityItem item = MarkedFlint(given);
    given.Context.SpawnsSeen.Add(7);
    var slot = new DummySlot();

    PickUpStep.Take(given.Context, slot, item, null);

    Assert.Equal("flat", slot.Itemstack.Attributes.GetString("stockForm"));
    Assert.Equal(2, slot.Itemstack.StackSize);
    Assert.NotSame(item.Itemstack, slot.Itemstack);
    Assert.Equal(0, given.Context.Pack!.Held("game:flint"));

    LiveFixture unseen = Packed(2);
    EntityItem outside = MarkedFlint(unseen);

    PickUpStep.Take(unseen.Context, new DummySlot(), outside, null);

    Assert.Equal(2, unseen.Context.Pack!.Held("game:flint"));
    Assert.Contains(7, unseen.Context.SpawnsSeen);
  }

  /// <summary>Fails when the code filter is skipped, so a flint is taken for a stick.</summary>
  [Fact]
  public void A_pickup_whose_code_matches_nothing_names_what_lies_there()
  {
    var fixture = new LiveFixture();
    Item flint = fixture.Test.RegisterItem("game:flint");
    fixture.Test.RegisterItem("game:stick");
    (ItemStack, Vec3d)[] lying =
    [
      (new ItemStack(flint, 2), new Vec3d(100.5, 50.5, 100.5)),
      (new ItemStack(flint, 3), new Vec3d(101.5, 50.5, 100.5)),
    ];

    int? chosen = SetupPickUp.Pick(
      lying,
      Cell,
      "game:stick",
      "plot (0, 0, 0)",
      out string? problem
    );

    Assert.Null(chosen);
    Assert.Equal(
      "no item entity within one cell of plot (0, 0, 0) is game:stick; there: game:flint x2, game:flint x3.",
      problem
    );
  }

  /// <summary>Fails when the heat refusal is skipped in the take (the first half does not throw), when the
  /// refusal changes the slot or <c>SpawnsSeen</c>, or when tongs do not allow the take or the click does not
  /// hold the taken stack.</summary>
  [Fact]
  public void A_hot_stack_is_taken_only_with_tongs_and_is_what_the_next_click_holds()
  {
    var fixture = new LiveFixture();
    Item ingot = fixture.Test.RegisterItem("game:ingot");
    var hot = new ItemStack(ingot, 1);
    ingot.SetTemperature(fixture.Test.World, hot, 900f);
    var tongs = new Item
    {
      Code = new AssetLocation("game:tongs"),
      ItemId = 990,
      Attributes = new JsonObject(
        JToken.Parse("""{ "heatResistant": true }""")
      ),
    };
    var item = Spawned(7, hot, 100.5);
    var slot = new DummySlot();

    var refusal = Assert.Throws<StepFailure>(() =>
      PickUpStep.Take(fixture.Context, slot, item, null)
    );

    Assert.Contains("too hot to hold", refusal.Message);
    Assert.Equal(Suspect.Scenario, refusal.Suspect);
    Assert.Null(slot.Itemstack);
    Assert.DoesNotContain(7, fixture.Context.SpawnsSeen);

    ItemStack taken = PickUpStep.Take(
      fixture.Context,
      slot,
      item,
      new ItemStack(tongs)
    );

    Assert.True(ingot.GetTemperature(fixture.Test.World, taken) > 850f);
    Assert.Same(taken, InteractStep.ClickStack(null, slot));
  }

  /// <summary>Fails when the live runner accepts a <c>pickup</c> inside a <c>during</c> (the refusal in
  /// <c>SetupFileModel.ParseDuring</c> removed).</summary>
  [Fact]
  public void A_pickup_in_a_during_is_refused_when_the_live_runner_loads_the_file()
  {
    var e = Assert.Throws<SetupFileException>(() =>
      LiveFixture.Case(
        """[{ "wait": 1 }]""",
        """{ during: [{ at: 2, steps: [{ pickup: { at: [0, 1, 0] } }] }] }"""
      )
    );

    Assert.Contains("\"pickup\" is not a step in a \"during\"", e.Message);
    Assert.Contains("a live during gives its spawns only at settle", e.Message);
  }
}
#endif
