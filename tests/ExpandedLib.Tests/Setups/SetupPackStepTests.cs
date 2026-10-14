#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What the steps draw from a setup's pack and give back to it, and what the end of the run
/// checks against <c>ends</c> (setups.md, The pack).</summary>
[Collection("Setups")]
public class SetupPackStepTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Box =
    "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";
  private const string Bench =
    "{\"place\": {\"id\": \"bench\", \"code\": \"setupcraft:bench\", \"at\": [0, 0, 0]}}";
  private const string Melt =
    "{\"fill\": {\"id\": \"melt\", \"code\": \"setupmelt:melt\", \"from\": [0, 0, 0], \"to\": [1, 0, 0]}}";

  private static readonly (string, string)[] Mods =
  [
    ("exlib", "0.8.4"),
    ("setupbox", "1.0.0"),
    ("setupcraft", "1.0.0"),
    ("setupmelt", "1.0.0"),
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

  private static void Register(TestWorld w) =>
    w.RegisterClass("SetupBox", typeof(BlockEntity))
      .RegisterBlockEntityFactory("SetupBox", () => new SetupBox())
      .RegisterClass("SetupMelt", typeof(BlockEntity))
      .RegisterBlockEntityFactory(
        "SetupMelt",
        () =>
          new SetupMoltenEntity
          {
            CellAmount = 40,
            CellMetalType = "game:ingot-iron",
            CellTemperature = 1500f,
          }
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

  private static SetupScene Build(
    string pack,
    string steps,
    System.Func<string, SetupFileModel?>? stages = null
  ) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\", \"setupcraft\", \"setupmelt\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}, \"pack\": "
          + pack
          + "}",
        "t.json"
      ),
      [Fixture("SetupBox"), Fixture("SetupCraft"), Fixture("SetupMelt")],
      null,
      Register,
      stages
    );

  private static string Refused(
    string pack,
    string steps,
    System.Func<string, SetupFileModel?>? stages = null
  ) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(pack, steps, stages))
      .Message;

  private static string Gathered(string scope, string gathered) =>
    "{\"scope\": \"" + scope + "\", \"gathered\": " + gathered + "}";

  // Fails when a draw is not made, so a place past the pack's blocks goes through.
  [Fact]
  public void A_short_draw_fails_the_step_naming_the_code_and_both_counts()
  {
    string message = Refused(
      Gathered("all", "{\"setupbox:box\": 1}"),
      "["
        + Box
        + ", {\"place\": {\"code\": \"setupbox:box\", \"at\": [2, 0, 0]}}]"
    );

    Assert.Equal(
      "t.json: steps[1]: the pack holds 0 of setupbox:box; the step takes 1.",
      message
    );
  }

  // Fails when a craft's product is not given to the pack, so the later insert draws from nothing.
  [Fact]
  public void A_crafts_product_is_drawn_by_a_later_insert()
  {
    using SetupScene scene = Build(
      Gathered("stacks", "{\"setupcraft:rod-*\": 2, \"setupcraft:plate\": 2}"),
      "["
        + Bench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}"
        + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:frame\", \"count\": 3, \"slot\": 2}}]"
    );

    var bench = (CraftFixtureBench)
      scene.World.GetBlockEntity(scene.PositionOf("bench"))!;
    Assert.Equal(3, bench.Inventory[2].Itemstack?.StackSize);
    string message = Refused(
      Gathered("stacks", "{\"setupcraft:rod-*\": 2, \"setupcraft:plate\": 1}"),
      "["
        + Bench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}]"
    );
    Assert.Contains(
      "the pack holds 1 of setupcraft:plate; the step takes 2.",
      message
    );
  }

  // Fails when the scope is ignored, so a stacks scope draws the placed block or an all scope draws
  // none.
  [Fact]
  public void The_stacks_scope_lets_a_place_through_and_the_all_scope_draws_it()
  {
    using SetupScene scene = Build(Gathered("stacks", "{}"), "[" + Box + "]");

    Assert.Equal(
      "setupbox:box",
      scene.World.GetBlock(scene.PositionOf("box")).Code.ToString()
    );
    Assert.Contains(
      "the pack holds 0 of setupbox:box",
      Refused(Gathered("all", "{}"), "[" + Box + "]")
    );
  }

  // Fails when a fill draws no metal, so a fill short of the pack's metal goes through.
  [Fact]
  public void A_fill_short_of_the_packs_metal_fails_naming_the_metal_units()
  {
    string message = Refused(
      Gathered("stacks", "{\"metal:iron\": 50}"),
      "[" + Melt + "]"
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds 50 of metal:iron; the step takes 80.",
      message
    );
  }

  // Fails when the end does not read the molten cells' metal and give it, so a declared metal end
  // reads 0.
  [Fact]
  public void The_metal_a_stage_ends_with_is_read_off_its_molten_cells()
  {
    string pack =
      "{\"scope\": \"stacks\", \"gathered\": {\"metal:iron\": 80}, \"ends\": {\"stacks\": {\"metal:iron\": 80}}}";
    using SetupScene scene = Build(pack, "[" + Melt + "]");
    scene.Run(Mods);

    using SetupScene over = Build(
      pack.Replace("\"metal:iron\": 80}}", "\"metal:iron\": 81}}"),
      "[" + Melt + "]"
    );
    string message = Assert
      .Throws<InvalidOperationException>(() => over.Run(Mods))
      .Message;
    Assert.Equal(
      "t.json: pack: the stage ends with 80 of metal:iron; it declares 81.",
      message
    );
  }

  // Fails when a standing block is not taken from the stage's list, or is taken twice.
  [Fact]
  public void A_block_an_earlier_stage_leaves_standing_places_free_once()
  {
    SetupFileModel stage = SetupFileModel.Parse(
      "{\"setup\": \"stage\", \"description\": \"d\", \"mods\": [], \"steps\": [], \"seconds\": 10, \"record\": {}, \"pack\": {\"scope\": \"all\", \"ends\": {\"stacks\": {}, \"standing\": [\"setupbox:box\"]}}}",
      "stage.json"
    );
    System.Func<string, SetupFileModel?> stages = id =>
      id == "exlib/setups/stage" ? stage : null;
    string pack =
      "{\"scope\": \"all\", \"after\": {\"exlib/setups/stage\": 1}}";

    string message = Refused(
      pack,
      "["
        + Box
        + ", {\"place\": {\"code\": \"setupbox:box\", \"at\": [2, 0, 0]}}]",
      stages
    );

    Assert.Contains("steps[1]: the pack holds 0 of setupbox:box", message);
  }

  // Fails when ends.standing is not checked against the blocks standing at the end of the run.
  [Fact]
  public void A_standing_block_the_run_lacks_fails_at_the_end()
  {
    string pack(string standing) =>
      "{\"scope\": \"stacks\", \"ends\": {\"stacks\": {}, \"standing\": ["
      + standing
      + "]}}";

    using SetupScene with = Build(pack("\"setupbox:b*\""), "[" + Box + "]");
    with.Run(Mods);

    using SetupScene without = Build(
      pack("\"setupmelt:melt\""),
      "[" + Box + "]"
    );
    string message = Assert
      .Throws<InvalidOperationException>(() => without.Run(Mods))
      .Message;
    Assert.Equal(
      "t.json: pack: the stage ends with no setupmelt:melt standing.",
      message
    );
  }

  // Fails when a layout run cell draws nothing, so a run past the pack's pipes goes through.
  [Fact]
  public void A_run_cell_draws_its_held_kind()
  {
    const string layout =
      "{\"legend\": {\"P\": {\"run\": \"main\", \"code\": \"platedpipes:pipe-plated-straight-we\", \"family\": [\"platedpipes:pipe-plated-straight-*\", \"platedpipes:pipe-plated-bend-*\"]}, \"o\": {\"open\": true}}, \"layers\": {\"0\": [\"oPPPo\"]}}";
    SetupFile File(int pipes) =>
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"platedpipes\"], \"layout\": "
          + layout
          + ", \"steps\": [], \"seconds\": 10, \"record\": {}, \"pack\": "
          + Gathered(
            "all",
            "{\"platedpipes:pipe-plated-straight-*\": " + pipes + "}"
          )
          + "}",
        "t.json"
      );
    string[] path = [Path.Combine(RepoPaths.Root, "samples", "PlatedPipes")];

    using SetupScene scene = SetupScene.Build(File(3), path);
    Assert.Equal(3, scene.CellsOf("main").Count);

    string message = Assert
      .Throws<InvalidOperationException>(() => SetupScene.Build(File(2), path))
      .Message;
    Assert.Equal(
      "t.json: run \"main\" legend \"P\": the pack holds 0 of platedpipes:pipe-plated-straight-we; the step takes 1.",
      message
    );
  }

  // Fails when a window put draws nothing, or a window take gives nothing back: the stack taken
  // out is then missing from the pack the next insert draws on.
  [Fact]
  public void A_window_put_draws_and_a_window_take_gives_back()
  {
    string steps(int taken) =>
      "["
      + Bench
      + ", {\"window\": {\"at\": {\"of\": \"bench\"}, \"open\": true, \"put\": {\"slot\": 0, \"stack\": \"setupcraft:plate\", \"count\": 2}, \"take\": {\"slot\": 0}, \"close\": true}}"
      + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:plate\", \"count\": "
      + taken
      + ", \"slot\": 2}}]";

    using SetupScene scene = Build(
      Gathered("stacks", "{\"setupcraft:plate\": 2}"),
      steps(2)
    );
    var bench = (CraftFixtureBench)
      scene.World.GetBlockEntity(scene.PositionOf("bench"))!;
    Assert.Equal(2, bench.Inventory[2].Itemstack?.StackSize);

    Assert.Contains(
      "steps[2]: the pack holds 2 of setupcraft:plate; the step takes 3.",
      Refused(Gathered("stacks", "{\"setupcraft:plate\": 2}"), steps(3))
    );
  }

  // Fails when an interact draws no held stack, or does not take back what the hand holds after.
  [Fact]
  public void An_interact_gives_back_the_stack_the_hand_still_holds()
  {
    using SetupScene scene = Build(
      Gathered("stacks", "{\"game:charcoal\": 2}"),
      "["
        + Box
        + ", {\"interact\": {\"at\": {\"of\": \"box\"}, \"with\": {\"stack\": \"game:charcoal\", \"count\": 2}}}"
        + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"game:charcoal\", \"count\": 2, \"slot\": 0}}]"
    );

    var box = (SetupBox)scene.World.GetBlockEntity(scene.PositionOf("box"))!;
    Assert.Equal(2, box.Inventory[0].Itemstack?.StackSize);
    Assert.Contains(
      "steps[2]: the pack holds 2 of game:charcoal; the step takes 3.",
      Refused(
        Gathered("stacks", "{\"game:charcoal\": 2}"),
        "["
          + Box
          + ", {\"interact\": {\"at\": {\"of\": \"box\"}, \"with\": {\"stack\": \"game:charcoal\", \"count\": 2}}}"
          + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"game:charcoal\", \"count\": 3, \"slot\": 0}}]"
      )
    );
  }

  private static string Spill(int dx, int dy = 0, int dz = 0) =>
    "{\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Spill\", \"args\": [\"setupcraft:plate\", 2, "
    + dx
    + ", "
    + dy
    + ", "
    + dz
    + "]}}";

  private static string InsertPlates(int count) =>
    "{\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": "
    + count
    + ", \"slot\": 0}}";

  // Fails when a step's draws are not taken, so a call the runner cannot see costs the pack nothing.
  [Fact]
  public void A_steps_draws_are_taken_before_it_runs()
  {
    string steps(int draws) =>
      "["
      + Box
      + ", {\"insert\": {\"at\": {\"of\": \"box\"}, \"stack\": \"setupcraft:plate\", \"count\": 2, \"slot\": 0}, \"draws\": {\"setupcraft:plate\": "
      + draws
      + "}}]";

    using SetupScene scene = Build(
      Gathered("stacks", "{\"setupcraft:plate\": 3}"),
      steps(1)
    );

    var box = (SetupBox)scene.World.GetBlockEntity(scene.PositionOf("box"))!;
    Assert.Equal(2, box.Inventory[0].Itemstack?.StackSize);
    Assert.Equal(
      "t.json: steps[1]: the pack holds 1 of setupcraft:plate; the step takes 2.",
      Refused(Gathered("stacks", "{\"setupcraft:plate\": 2}"), steps(1))
    );
  }

  // Fails when a step's gives are not added to the pack, so the stack it names is not there to draw.
  [Fact]
  public void A_steps_gives_are_drawn_by_a_later_step()
  {
    string steps(int count) =>
      "["
      + Box
      + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Spill\", \"args\": [\"setupcraft:plate\", 1, 9, 0, 0]}, \"gives\": {\"setupcraft:plate\": 2}}"
      + ", "
      + InsertPlates(count)
      + "]";

    using SetupScene scene = Build(Gathered("stacks", "{}"), steps(2));

    var box = (SetupBox)scene.World.GetBlockEntity(scene.PositionOf("box"))!;
    Assert.Equal(2, box.Inventory[0].Itemstack?.StackSize);
    Assert.Equal(
      "t.json: steps[2]: the pack holds 2 of setupcraft:plate; the step takes 3.",
      Refused(Gathered("stacks", "{}"), steps(3))
    );
  }

  // Fails when an item entity spawned beside the case's blocks is not given to the pack at the
  // step's end.
  [Fact]
  public void A_spawned_stack_beside_the_blocks_is_given()
  {
    using SetupScene scene = Build(
      Gathered("stacks", "{}"),
      "[" + Box + ", " + Spill(1) + ", " + InsertPlates(2) + "]"
    );

    var box = (SetupBox)scene.World.GetBlockEntity(scene.PositionOf("box"))!;
    Assert.Equal(2, box.Inventory[0].Itemstack?.StackSize);
  }

  // Fails when every spawn is given whatever its position, so the bounds are ignored.
  [Fact]
  public void A_spawned_stack_outside_the_blocks_bounds_is_not_given()
  {
    string message = Refused(
      Gathered("stacks", "{}"),
      "[" + Box + ", " + Spill(2) + ", " + InsertPlates(2) + "]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 0 of setupcraft:plate; the step takes 2.",
      message
    );
  }

  // Fails when a spawn one cell past the bounds on any side is given, so that side's guard is
  // unproven.
  [Theory]
  [InlineData(-2, 0, 0)]
  [InlineData(0, 2, 0)]
  [InlineData(0, -2, 0)]
  [InlineData(0, 0, 2)]
  [InlineData(0, 0, -2)]
  public void A_spawned_stack_past_each_bound_is_not_given(
    int dx,
    int dy,
    int dz
  )
  {
    string message = Refused(
      Gathered("stacks", "{}"),
      "[" + Box + ", " + Spill(dx, dy, dz) + ", " + InsertPlates(2) + "]"
    );

    Assert.Equal(
      "t.json: steps[2]: the pack holds 0 of setupcraft:plate; the step takes 2.",
      message
    );
  }

  // Fails when a spawn made by a tick is not given by the end of that second, so a stage that
  // ends with it reads 0.
  [Fact]
  public void A_spawn_made_by_a_tick_is_given_by_the_end_of_that_second()
  {
    using SetupScene scene = Build(
      "{\"scope\": \"stacks\", \"gathered\": {}, \"ends\": {\"stacks\": {\"setupbox:box\": 1}}}",
      "["
        + Box
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Arm\"}}]"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      scene.World.Spawns.Count(s => s.Stack.Collectible.Code.Path == "box"),
      pack.End["setupbox:box"]
    );
  }

  // Fails when a fill's metal is read only from the cells it names, so an unnamed fill ends with 0.
  [Fact]
  public void The_metal_of_an_unnamed_fill_is_read_at_the_end()
  {
    using SetupScene scene = Build(
      "{\"scope\": \"stacks\", \"gathered\": {\"metal:iron\": 80}, \"ends\": {\"stacks\": {\"metal:iron\": 80}}}",
      "[{\"fill\": {\"code\": \"setupmelt:melt\", \"from\": [0, 0, 0], \"to\": [1, 0, 0]}}]"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(80, pack.End["metal:iron"]);
  }

  // Fails when a concrete ingredient is laid as the first registered variant rather than the one the
  // pack holds, so the craft draws a rod the pack does not have.
  [Fact]
  public void A_wildcard_ingredient_is_laid_as_the_variant_the_pack_holds()
  {
    using SetupScene scene = Build(
      Gathered(
        "stacks",
        "{\"setupcraft:rod-iron\": 2, \"setupcraft:plate\": 2}"
      ),
      "["
        + Bench
        + ", {\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:frame\"}}"
        + ", {\"insert\": {\"at\": {\"of\": \"bench\"}, \"stack\": \"setupcraft:frame\", \"count\": 3, \"slot\": 2}}]"
    );

    var bench = (CraftFixtureBench)
      scene.World.GetBlockEntity(scene.PositionOf("bench"))!;
    Assert.Equal(3, bench.Inventory[2].Itemstack?.StackSize);
  }

  // Fails when a spawn is given again at the end of each later step.
  [Fact]
  public void A_spawned_stack_is_given_once()
  {
    string message = Refused(
      Gathered("stacks", "{}"),
      "["
        + Box
        + ", "
        + Spill(1)
        + ", "
        + InsertPlates(2)
        + ", "
        + InsertPlates(1)
        + "]"
    );

    Assert.Equal(
      "t.json: steps[3]: the pack holds 0 of setupcraft:plate; the step takes 1.",
      message
    );
  }
}
#endif
