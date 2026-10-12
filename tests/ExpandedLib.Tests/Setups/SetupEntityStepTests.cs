#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What the <c>fill</c>, <c>insert</c>, <c>set</c> and <c>call</c> steps do to a block and
/// its entity, and what fails them (setups.md, Steps the setups add).</summary>
[Collection("Setups")]
public class SetupEntityStepTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupBox"
  );

  private static SetupScene Build(SetupFile file) =>
    SetupScene.Build(
      file,
      [FixturePath],
      null,
      w =>
        w.RegisterClass("SetupBox", typeof(BlockEntity))
          .RegisterBlockEntityFactory(
            "SetupBox",
            () =>
            {
              var box = new SetupBox();
              box.Behaviors.Add(new BoxBehaviour(box));
              return box;
            }
          )
    );

  private static SetupScene BuildSample() =>
    Build(
      SetupFile.Load(
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Files",
          "entity-steps.json"
        )
      )
    );

  private static string Refused(string steps) =>
    Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          SetupFile.Parse(
            "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": "
              + steps
              + ", \"seconds\": 10, \"record\": {}}",
            "t.json"
          )
        )
      )
      .Message;

  private const string PlaceBox =
    "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";

  private static SetupBox BoxOf(SetupScene scene) =>
    Assert.IsType<SetupBox>(
      scene.World.GetBlockEntity(scene.PositionOf("box"))
    );

  // Fails when a fill over a cell that is not air is let through instead of failing the step.
  [Fact]
  public void A_fill_over_a_cell_that_is_not_air_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"fill\": {\"code\": \"setupbox:slab\", \"from\": [0, 0, 0], \"to\": [1, 0, 0]}}]"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("(0, 0, 0) holds setupbox:box, not air", message);
  }

  // Fails when a fill replaces a cell already holding its own code, or fails on it.
  [Fact]
  public void A_fill_over_cells_already_holding_its_code_passes()
  {
    using SetupScene scene = Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": ["
          + "{\"fill\": {\"code\": \"setupbox:slab\", \"from\": [0, 0, 0], \"to\": [1, 0, 0]}}, "
          + "{\"fill\": {\"code\": \"setupbox:slab\", \"from\": [0, 0, 0], \"to\": [2, 0, 0]}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      )
    );

    Assert.Equal(
      "setupbox:slab",
      scene.World.GetBlock(new BlockPos(2, 0, 0)).Code.ToString()
    );
  }

  // Fails when insert ignores its slot or its count.
  [Fact]
  public void An_insert_puts_the_stack_into_the_named_slot()
  {
    using SetupScene scene = BuildSample();

    SetupBox box = BoxOf(scene);

    Assert.Null(box.Inventory[0].Itemstack);
    Assert.Equal(3, box.Inventory[1].Itemstack?.StackSize);
    Assert.Equal(
      "setupbox:slab",
      box.Inventory[1].Itemstack?.Collectible.Code.ToString()
    );
  }

  // Fails when an insert without a slot takes slot 0 instead of the slot the inventory picks: the
  // inventory prefers the slot already holding the stack.
  [Fact]
  public void An_insert_without_a_slot_takes_the_slot_the_inventory_picks()
  {
    using SetupScene scene = Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": ["
          + PlaceBox
          + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupbox:slab\", \"slot\": 1}}"
          + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupbox:slab\"}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      )
    );

    SetupBox box = BoxOf(scene);

    Assert.Null(box.Inventory[0].Itemstack);
    Assert.Equal(2, box.Inventory[1].Itemstack?.StackSize);
  }

  // Fails when insert drops its temperature.
  [Fact]
  public void An_insert_with_a_temperature_heats_the_stack()
  {
    using SetupScene scene = Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": ["
          + PlaceBox
          + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupbox:slab\", \"slot\": 0, \"temperature\": 850}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      )
    );

    ItemStack stack = BoxOf(scene).Inventory[0].Itemstack!;

    Assert.Equal(
      850f,
      stack.Collectible.GetTemperature(scene.World.World, stack)
    );
  }

  // Fails when a stack the slot cannot take in full is counted as inserted.
  [Fact]
  public void An_insert_the_slot_refuses_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupbox:slab\", \"count\": 3, \"slot\": 0}}"
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupbox:slab\", \"count\": 3, \"slot\": 0}}]"
    );

    Assert.Contains("took 1 of 3 setupbox:slab", message);
  }

  // Fails when the slot index is not checked against the inventory.
  [Fact]
  public void An_insert_past_the_last_slot_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupbox:slab\", \"slot\": 5}}]"
    );

    Assert.Contains("slot 5 is past the inventory's 2", message);
  }

  // Fails when set member skips private fields, converts an enum by number or never marks the
  // entity dirty.
  [Fact]
  public void A_set_member_writes_a_private_field_and_an_enum_by_name_and_marks_dirty()
  {
    using SetupScene scene = BuildSample();

    SetupBox box = BoxOf(scene);

    Assert.Equal(7, box.CountSeen);
    Assert.Equal(BoxState.Running, box.StateSeen);
    Assert.Equal(2, box.Dirtied);
  }

  // Fails when an enum name outside the type is converted to a default member.
  [Fact]
  public void A_set_member_enum_name_the_type_lacks_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"set\": {\"at\": {\"of\": \"box\"}, \"member\": {\"_state\": \"Broken\"}}}]"
    );

    Assert.Contains("\"Broken\" is not a member of BoxState", message);
  }

  // Fails when a member the entity lacks is skipped silently.
  [Fact]
  public void A_set_member_the_entity_lacks_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"set\": {\"at\": {\"of\": \"box\"}, \"member\": {\"_nothing\": 1}}}]"
    );

    Assert.Contains("no writable member \"_nothing\"", message);
  }

  // Fails when set tree writes nothing back through FromTreeAttributes.
  [Fact]
  public void A_set_tree_writes_a_key_through_the_entity_tree()
  {
    using SetupScene scene = BuildSample();

    Assert.Equal(4, BoxOf(scene).Level);
  }

  // Fails when a tree key the entity does not hold is added instead of failing.
  [Fact]
  public void A_set_tree_key_the_entity_lacks_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"set\": {\"at\": {\"of\": \"box\"}, \"tree\": {\"nothing\": 1}}}]"
    );

    Assert.Contains("has no tree key \"nothing\"", message);
  }

  // Fails when call passes its arguments unconverted: the enum name, the stack and the player
  // must reach Poke as their types, with the count and the player the file named.
  [Fact]
  public void A_call_converts_its_arguments_and_checks_what_it_returns()
  {
    using SetupScene scene = BuildSample();

    var (n, state, stack, player) = BoxOf(scene).Poked!.Value;

    Assert.Equal(2, n);
    Assert.Equal(BoxState.Running, state);
    Assert.Equal(5, stack.StackSize);
    Assert.Equal("setupbox:slab", stack.Collectible.Code.ToString());
    Assert.Equal("test", player.PlayerUID);
  }

  // Fails when a method written Behaviour.Method is looked up on the entity instead of its behaviour,
  // or the behaviour is not found by type name.
  [Fact]
  public void A_call_on_a_behaviour_reaches_the_behaviour()
  {
    using SetupScene scene = Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": ["
          + PlaceBox
          + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"BoxBehaviour.Nudge\", \"args\": [3], \"returns\": 3}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      )
    );

    Assert.Equal(3, BoxOf(scene).GetBehavior<BoxBehaviour>()!.Nudged);
  }

  // Fails when a behaviour the entity lacks is skipped silently.
  [Fact]
  public void A_call_on_a_behaviour_the_entity_lacks_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Missing.Nudge\", \"args\": [3]}}]"
    );

    Assert.Contains("has no behaviour Missing", message);
  }

  // Fails when set tree retypes a key as an int whatever it held, or writes the wrong attribute for
  // a long, float, double, bool or string.
  [Fact]
  public void A_set_tree_writes_each_attribute_type_it_holds()
  {
    using SetupScene scene = Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": ["
          + PlaceBox
          + ", {\"set\": {\"at\": {\"of\": \"box\"}, \"tree\": {\"big\": 5000000000, \"ratio\": 0.5, \"exact\": 2.25, \"on\": true, \"label\": \"hot\"}}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      )
    );

    SetupBox box = BoxOf(scene);

    Assert.Equal(5000000000L, box.Big);
    Assert.Equal(0.5f, box.Ratio);
    Assert.Equal(2.25, box.Exact);
    Assert.True(box.On);
    Assert.Equal("hot", box.Label);
  }

  // Fails when set tree overwrites an attribute of a type it cannot write.
  [Fact]
  public void A_set_tree_key_of_a_type_set_cannot_write_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"set\": {\"at\": {\"of\": \"box\"}, \"tree\": {\"raw\": 1}}}]"
    );

    Assert.Contains("tree key \"raw\" holds a ByteArrayAttribute", message);
  }

  // Fails when returns is not compared with the value the method gave.
  [Fact]
  public void A_call_whose_return_value_differs_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Poke\", \"args\": [1, \"Idle\", {\"stack\": \"setupbox:slab\"}, \"player\"], \"returns\": \"nope\"}}]"
    );

    Assert.Contains("Poke returned 1:Idle:1:test, not nope", message);
  }

  // Fails when a method is matched by name alone, ignoring the argument count.
  [Fact]
  public void A_call_of_a_method_the_entity_lacks_fails_the_step()
  {
    string message = Refused(
      "["
        + PlaceBox
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Poke\", \"args\": [1]}}]"
    );

    Assert.Contains("no public method Poke taking 1 arguments", message);
  }

  // Fails when a step at a cell with no block entity is skipped.
  [Fact]
  public void A_step_at_a_cell_with_no_entity_fails_naming_the_block()
  {
    string message = Refused(
      "[{\"fill\": {\"code\": \"setupbox:slab\", \"from\": [0, 0, 0], \"to\": [0, 0, 0]}}, "
        + "{\"set\": {\"at\": [0, 0, 0], \"member\": {\"_count\": 1}}}]"
    );

    Assert.Contains("holds setupbox:slab, which has no block entity", message);
  }
}
#endif
