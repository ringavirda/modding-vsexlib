#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Catalogues;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Scenarios;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What a setup's steps are walked against before the first one runs: the pack as the
/// steps would draw it down, the load's registrations and the recipes' inputs (setups.md, The
/// harness reader). Each case carries a second place at the first box's cell, which only the run
/// can refuse, so a message naming a later step comes from the walk and not from the run.</summary>
[Collection("Setups")]
public class SetupPreflightTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Box =
    "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";
  private const string Bench =
    "{\"place\": {\"id\": \"bench\", \"code\": \"setupcraft:bench\", \"at\": [0, 0, 0]}}";
  private const string SeqBench =
    "{\"place\": {\"id\": \"bench\", \"code\": \"setupcraft:seqbench\", \"at\": [0, 0, 0]}}";
  private const string Occupied =
    "{\"place\": {\"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";
  private const string OccupiedBench =
    "{\"place\": {\"code\": \"setupcraft:bench\", \"at\": [0, 0, 0]}}";
  private const string Sequences =
    "{\"machine\": \"bench\", \"sequences\": [{\"recipe\": \"setupcraft:frame\", \"steps\": ["
    + "{\"tool\": \"setupcraft:mallet\", \"seconds\": 1}, "
    + "{\"tool\": \"setupcraft:mallet\", \"offhand\": \"setupcraft:stud\", \"seconds\": 1}]}]}";

  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Fixtures",
      name
    );

  private static SetupFile Variant(bool chosen, SetupFile file) =>
    chosen ? file.ForVariant("v") : file;

  private static string Pack(string scope, string gathered) =>
    "{\"scope\": \"" + scope + "\", \"gathered\": " + gathered + "}";

  private static string Refused(
    string pack,
    string steps,
    string? layout = null,
    string? sequences = null,
    string? variants = null
  ) =>
    Assert
      .Throws<InvalidOperationException>(() =>
        SetupScene.Build(
          Variant(
            variants != null,
            SetupFile.Parse(
              "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\", \"setupcraft\"], "
                + (layout == null ? "" : "\"layout\": " + layout + ", ")
                + "\"steps\": "
                + steps
                + ", \"seconds\": 10, \"record\": {}, \"pack\": "
                + pack
                + (variants == null ? "" : ", \"variants\": " + variants)
                + "}",
              "t.json"
            )
          ),
          [Fixture("SetupBox"), Fixture("SetupCraft")],
          sequences == null
            ? null
            : _ =>
              CraftSequenceLoader.Load([
                ("setupcraft:config/craftsequences/bench.json", sequences),
              ]),
          w =>
          {
            CraftStepTests.Register(w);
            w.RegisterClass(
                "SetupBox",
                typeof(Vintagestory.API.Common.BlockEntity)
              )
              .RegisterBlockEntityFactory("SetupBox", () => new SetupBox());
          }
        )
      )
      .Message;

  private const string Plates = "{\"setupcraft:plate\": 2}";
  private const string Frames =
    "{\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 2}";

  // Fails when the walk stops at its first problem, or when a problem is left to the run.
  [Fact]
  public void A_setup_with_three_problems_reports_all_three_in_one_run()
  {
    string message = Refused(
      Pack("stacks", Plates),
      "["
        + Box
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}"
        + ", {\"place\": {\"code\": \"setupbox:ghost\", \"at\": [2, 0, 0]}}"
        + ", {\"complete\": {\"of\": \"box\"}}]"
    );

    Assert.Equal(
      "t.json: 3 problems before any step ran:\n"
        + "  steps[1]: the pack holds 2 of setupcraft:plate; the step takes 3.\n"
        + "  steps[2]: \"setupbox:ghost\" is not registered by the load.\n"
        + "  steps[3]: setupbox:box at (0, 0, 0) is neither a multiblock nor a construction, so it has nothing to complete.",
      message
    );
  }

  // Fails when a place's code is not resolved in the walk.
  [Fact]
  public void A_place_of_a_code_the_load_did_not_register_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", "{}"),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"place\": {\"code\": \"setupbox:ghost\", \"at\": [2, 0, 0]}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: \"setupbox:ghost\" is not registered by the load.",
      message
    );
  }

  // Fails when a place is not drawn in the walk, so the third box passes it.
  [Fact]
  public void A_place_past_the_packs_blocks_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("all", "{\"setupbox:box\": 2}"),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"place\": {\"code\": \"setupbox:box\", \"at\": [2, 0, 0]}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 0 of setupbox:box; the step takes 1.",
      message
    );
  }

  // Fails when a layout block is not drawn in the walk.
  [Fact]
  public void A_layout_block_past_the_packs_blocks_is_refused_with_the_steps_problems()
  {
    string message = Refused(
      Pack("all", "{\"setupbox:box\": 1}"),
      "[{\"place\": {\"code\": \"setupbox:ghost\", \"at\": [0, 2, 0]}}]",
      "{\"legend\": {\"B\": {\"code\": \"setupbox:box\"}}, \"layers\": {\"0\": [\"BB\"]}}"
    );

    Assert.Equal(
      "t.json: 2 problems before any step ran:\n"
        + "  legend \"B\": the pack holds 0 of setupbox:box; the step takes 1.\n"
        + "  steps[0]: \"setupbox:ghost\" is not registered by the load.",
      message
    );
  }

  // Fails when an insert is not drawn in the walk.
  [Fact]
  public void An_insert_past_the_packs_stacks_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", Plates),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 2 of setupcraft:plate; the step takes 3.",
      message
    );
  }

  // Fails when a step's own draws are not taken in the walk.
  [Fact]
  public void A_steps_draws_past_the_packs_stacks_are_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", Plates),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Arm\"}, \"draws\": {\"setupcraft:plate\": 3}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 2 of setupcraft:plate; the step takes 3.",
      message
    );
  }

  // Fails when a held stack is not drawn in the walk, or a kept one is given back to it.
  [Fact]
  public void A_kept_stack_is_not_given_back_until_a_step_releases_the_hand()
  {
    string keep =
      "{\"interact\": {\"at\": {\"of\": \"box\"}, \"with\": {\"stack\": \"setupcraft:plate\", \"count\": 2}, \"keep\": true}}";
    string message = Refused(
      Pack("stacks", Plates),
      "["
        + Box
        + ", "
        + Occupied
        + ", "
        + keep
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": 1, \"slot\": 0}}]"
    );

    Assert.Equal(
      "t.json: steps[3]: the pack holds 0 of setupcraft:plate; the step takes 1.",
      message
    );
  }

  // Fails when a craft's product is not given in the walk, so the insert of it is refused.
  [Fact]
  public void A_crafts_product_is_in_the_walks_pack_for_a_later_insert()
  {
    string message = Refused(
      Pack("stacks", Frames),
      "["
        + Bench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}"
        + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:frame\", \"count\": 4, \"slot\": 2}}]"
    );

    Assert.Equal(
      "t.json: steps[3]: the pack holds 3 of setupcraft:frame; the step takes 4.",
      message
    );
  }

  // Fails when a craft's bill is not drawn in the walk.
  [Fact]
  public void A_craft_past_the_packs_stacks_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", "{\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 1}"),
      "["
        + Bench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 1 of setupcraft:plate; the step takes 2.",
      message
    );
  }

  // Fails when a with that names no input of the order is not read in the walk.
  [Fact]
  public void A_with_that_is_no_input_of_the_order_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", Frames),
      "["
        + Bench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\", \"with\": {\"brick\": \"setupcraft:plate\"}}}]"
    );

    Assert.StartsWith(
      "t.json: steps[2]: with brick is no input of setupcraft:frame; its inputs are ",
      message
    );
  }

  // Fails when a station with no order for the output is not read in the walk.
  [Fact]
  public void A_craft_no_order_of_the_station_makes_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", Frames),
      "["
        + Bench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:plate\"}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: setupcraft:bench at (0, 0, 0) has no order that makes setupcraft:plate; made at: no loaded station.",
      message
    );
  }

  // Fails when the entity of the block a craft is at is not asked whether it is a station.
  [Fact]
  public void A_craft_at_a_block_that_is_no_station_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", Frames),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"craft\": {\"at\": {\"of\": \"box\"}, \"output\": \"setupcraft:frame\"}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: setupbox:box at (0, 0, 0) is not a craft station.",
      message
    );
  }

  // Fails when the sequence's tools are not looked for in the walk, or are not given back after
  // each step so the second step finds the first's mallet gone.
  [Fact]
  public void A_sequence_tool_the_pack_lacks_is_refused_for_each_step_before_the_run()
  {
    string message = Refused(
      Pack(
        "stacks",
        "{\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 2, \"setupcraft:stud\": 1}"
      ),
      "["
        + SeqBench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}]",
      null,
      Sequences
    );

    Assert.Equal(
      "t.json: 2 problems before any step ran:\n"
        + "  steps[2]: step 1 of setupcraft:frame: the pack holds no setupcraft:mallet.\n"
        + "  steps[2]: step 2 of setupcraft:frame: the pack holds no setupcraft:mallet.",
      message
    );
  }

  // Fails when a pack tool that is held and returned twice is counted as spent, so the second step
  // reports a mallet the pack still has.
  [Fact]
  public void A_sequence_tool_the_pack_holds_is_given_back_after_each_step()
  {
    string message = Refused(
      Pack(
        "stacks",
        "{\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 2, \"setupcraft:mallet\": 1}"
      ),
      "["
        + SeqBench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}]",
      null,
      Sequences
    );

    Assert.Equal(
      "t.json: steps[2]: step 2 of setupcraft:frame: the pack holds no setupcraft:stud.",
      message
    );
  }

  // Fails when a complete of a block with no construction or structure is not read in the walk.
  [Fact]
  public void A_complete_of_a_block_with_nothing_to_complete_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", "{}"),
      "[" + Box + ", " + Occupied + ", {\"complete\": {\"of\": \"box\"}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: setupbox:box at (0, 0, 0) is neither a multiblock nor a construction, so it has nothing to complete.",
      message
    );
  }

  // Fails when a step that ticks the world does not stop the walk reporting shortages, so a stack
  // the tick spawns is refused before the run gives it.
  [Fact]
  public void A_shortage_after_a_step_that_ticks_the_world_is_left_to_the_run()
  {
    string message = Refused(
      Pack("stacks", "{}"),
      "["
        + Box
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Spill\", \"args\": [\"setupcraft:plate\", 2, 1, 0, 0]}}"
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 2 of setupcraft:plate; the step takes 3.",
      message
    );
  }

  private const string Ghost =
    "{\"place\": {\"code\": \"setupbox:ghost\", \"at\": [2, 0, 0]}}";
  private const string Insert3 =
    "{\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}";

  private static string TaintedBy(string step) =>
    Refused(
      Pack("stacks", Plates),
      "[" + Box + ", " + Occupied + ", " + step + ", " + Insert3 + "]"
    );

  // Fails when a timed interact does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_timed_interact_is_left_to_the_run()
  {
    string message = TaintedBy(
      "{\"interact\": {\"at\": {\"of\": \"box\"}, \"seconds\": 1}}"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a controlled interact does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_controlled_interact_is_left_to_the_run()
  {
    string message = TaintedBy(
      "{\"interact\": {\"at\": {\"of\": \"box\"}, \"control\": \"sneak\"}}"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a window take does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_window_take_is_left_to_the_run()
  {
    string message = TaintedBy(
      "{\"window\": {\"at\": {\"of\": \"box\"}, \"take\": {\"slot\": 0}}}"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a window press does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_window_press_is_left_to_the_run()
  {
    string message = TaintedBy(
      "{\"window\": {\"at\": {\"of\": \"box\"}, \"press\": {\"id\": 2000}}}"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a craft at a position the walk cannot resolve does not stop the walk reporting
  // shortages.
  [Fact]
  public void A_shortage_after_a_craft_at_an_unresolved_position_is_left_to_the_run()
  {
    string message = TaintedBy(
      "{\"craft\": {\"at\": {\"of\": \"nowhere\"}, \"output\": \"setupcraft:frame\"}}"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a craft at a cell the walk did not place does not stop the walk reporting
  // shortages.
  [Fact]
  public void A_shortage_after_a_craft_at_an_unplaced_cell_is_left_to_the_run()
  {
    string message = TaintedBy(
      "{\"craft\": {\"at\": [5, 0, 0], \"output\": \"setupcraft:frame\"}}"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a craft at a block with no entity does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_craft_at_a_block_with_no_entity_is_left_to_the_run()
  {
    string message = Refused(
      Pack("stacks", Plates),
      "[{\"place\": {\"id\": \"slab\", \"code\": \"setupbox:slab\", \"at\": [0, 0, 0]}}, "
        + "{\"craft\": {\"at\": {\"of\": \"slab\"}, \"output\": \"setupcraft:frame\"}}, "
        + Insert3
        + "]"
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a station whose orders cannot be read does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_craft_at_a_station_that_cannot_list_its_orders_is_left_to_the_run()
  {
    CraftFixtureBench.ThrowsOrders = true;
    try
    {
      string message = Refused(
        Pack("stacks", Frames),
        "["
          + Bench
          + ", "
          + OccupiedBench
          + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}"
          + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}]"
      );

      Assert.DoesNotContain("the pack holds", message);
    }
    finally
    {
      CraftFixtureBench.ThrowsOrders = false;
    }
  }

  // Fails when a craft with a sequence does not stop the walk reporting shortages, since the
  // sequence's work gives what the walk cannot know.
  [Fact]
  public void A_shortage_after_a_sequence_craft_is_left_to_the_run()
  {
    string message = Refused(
      Pack(
        "stacks",
        "{\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 2, \"setupcraft:mallet\": 1, \"setupcraft:stud\": 1}"
      ),
      "["
        + SeqBench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}"
        + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}]",
      null,
      Sequences
    );

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when a complete of a multiblock does not stop the walk reporting shortages.
  [Fact]
  public void A_shortage_after_a_complete_of_a_multiblock_is_left_to_the_run()
  {
    var stack = Newtonsoft.Json.Linq.JObject.Parse(
      File.ReadAllText(
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Files",
          "smoke-stack.json"
        )
      )
    );
    const string place =
      "{\"place\": {\"code\": \"smokestack:smokestack-intake-tier3-n\", \"at\": [1, 1, 0]}";
    stack["pack"] = Newtonsoft.Json.Linq.JObject.Parse(
      Pack("stacks", "{\"game:stick\": 2}")
    );
    stack["steps"] = Newtonsoft.Json.Linq.JArray.Parse(
      "[" + place + "}, " + place + ", \"draws\": {\"game:stick\": 3}}]"
    );
    var message = Assert
      .Throws<InvalidOperationException>(() =>
        SetupScene.Build(
          SetupFile.Parse(stack.ToString(), "t.json"),
          [Path.Combine(RepoPaths.Root, "samples", "SmokeStack")]
        )
      )
      .Message;

    Assert.DoesNotContain("the pack holds", message);
  }

  // Fails when the walk reports a station's missing order and also the shortage after it, which
  // the walk cannot place.
  [Fact]
  public void A_shortage_after_a_craft_no_order_makes_is_left_to_the_run()
  {
    string message = Refused(
      Pack("stacks", Frames),
      "["
        + Bench
        + ", "
        + OccupiedBench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:plate\"}}"
        + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:plate\", \"count\": 3, \"slot\": 0}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: setupcraft:bench at (0, 0, 0) has no order that makes setupcraft:plate; made at: no loaded station.",
      message
    );
  }

  // Fails when a craft at a block that is no station leaves the walk reporting later shortages.
  [Fact]
  public void A_shortage_after_a_craft_at_a_block_that_is_no_station_is_left_to_the_run()
  {
    string message = Refused(
      Pack("stacks", Frames),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"craft\": {\"at\": {\"of\": \"box\"}, \"output\": \"setupcraft:frame\"}}, "
        + Insert3
        + "]"
    );

    Assert.Equal(
      "t.json: steps[2]: setupbox:box at (0, 0, 0) is not a craft station.",
      message
    );
  }

  // Fails when a block a placer takes is read for a complete, which the placer answers.
  [Fact]
  public void A_complete_of_a_block_a_placer_takes_is_left_to_the_run()
  {
    SetupPlacers.Register(new TakingPlacer("setupbox:box"));
    string message = Refused(
      Pack("stacks", "{}"),
      "[" + Box + ", " + Ghost + ", {\"complete\": {\"of\": \"box\"}}]"
    );

    Assert.Equal(
      "t.json: steps[1]: \"setupbox:ghost\" is not registered by the load.",
      message
    );
  }

  // Fails when a craft at a block a placer takes is walked as if the world held its entity.
  [Fact]
  public void A_craft_at_a_block_a_placer_takes_is_left_to_the_run()
  {
    SetupPlacers.Register(new TakingPlacer("setupcraft:bench"));
    string message = Refused(
      Pack("stacks", "{\"setupcraft:plate\": 1}"),
      "["
        + Bench
        + ", "
        + Ghost
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}]"
    );

    Assert.Equal(
      "t.json: steps[1]: \"setupbox:ghost\" is not registered by the load.",
      message
    );
  }

  private sealed class TakingPlacer(string code) : ISetupPlacer
  {
    public bool Takes(string candidate) => candidate == code;

    public string? StandIn(string candidate) => null;

    public void Place(
      SetupScene scene,
      Vintagestory.API.Common.Block block,
      Vintagestory.API.MathTools.BlockPos pos
    ) => scene.World.Accessor.SetBlock(block.BlockId, pos);
  }

  // Fails when a place whose position cannot be resolved stops the walk before the later steps.
  [Fact]
  public void A_place_at_an_unresolved_position_does_not_hide_the_later_steps_problems()
  {
    string message = Refused(
      Pack("stacks", "{}"),
      "[{\"place\": {\"code\": \"setupbox:box\", \"at\": {\"of\": \"later\"}}}, "
        + "{\"place\": {\"id\": \"later\", \"code\": \"setupbox:ghost\", \"at\": [2, 0, 0]}}]"
    );

    Assert.Equal(
      "t.json: steps[1]: \"setupbox:ghost\" is not registered by the load.",
      message
    );
  }

  // Fails when a window put draws one stack for the whole slot range, or none.
  [Fact]
  public void A_window_put_over_a_slot_range_draws_a_stack_for_each_slot()
  {
    string message = Refused(
      Pack("stacks", "{\"setupcraft:plate\": 3}"),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"window\": {\"at\": {\"of\": \"box\"}, \"put\": {\"slot\": [0, 1], \"stack\": \"setupcraft:plate\", \"count\": 2}}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 1 of setupcraft:plate; the step takes 2.",
      message
    );
  }

  // Fails when a craft with no station is not walked against the hand grid.
  [Fact]
  public void A_hand_grid_craft_past_the_packs_stacks_is_refused_before_the_run()
  {
    string message = Refused(
      Pack("stacks", "{\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 1}"),
      "["
        + Box
        + ", "
        + Occupied
        + ", {\"craft\": {\"output\": \"setupcraft:frame\"}}]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 1 of setupcraft:plate; the step takes 2.",
      message
    );
  }

  // Fails when steps a variant inserts after a layout block are not walked.
  [Fact]
  public void A_variants_steps_after_a_layout_block_are_walked_with_the_steps()
  {
    string message = Refused(
      Pack("stacks", "{}"),
      "[" + Ghost + "]",
      "{\"legend\": {\"B\": {\"id\": \"lay\", \"code\": \"setupbox:box\"}}, \"layers\": {\"1\": [\"B\"]}}",
      null,
      "{\"v\": {\"after\": {\"lay\": [" + Ghost + "]}}}"
    );

    Assert.StartsWith("t.json", message);
    Assert.Contains("2 problems before any step ran:", message);
  }

  // Fails when steps a variant inserts after a layout run are not walked.
  [Fact]
  public void A_variants_steps_after_a_layout_run_are_walked_with_the_steps()
  {
    string ghost =
      "{\"place\": {\"code\": \"platedpipes:ghost\", \"at\": [0, 2, 0]}}";
    var file = Newtonsoft.Json.Linq.JObject.Parse(
      File.ReadAllText(
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Files",
          "pipe-run.json"
        )
      )
    );
    file["steps"] = Newtonsoft.Json.Linq.JArray.Parse("[" + ghost + "]");
    file["variants"] = Newtonsoft.Json.Linq.JObject.Parse(
      "{\"v\": {\"after\": {\"main\": [" + ghost + "]}}}"
    );
    var message = Assert
      .Throws<InvalidOperationException>(() =>
        SetupScene.Build(
          SetupFile.Parse(file.ToString(), "t.json").ForVariant("v"),
          [Path.Combine(RepoPaths.Root, "samples", "PlatedPipes")]
        )
      )
      .Message;

    Assert.Contains("2 problems before any step ran:", message);
  }
}
#endif
