#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

internal sealed class CraftFixtureBench
  : BlockEntityMachineStation,
    ICraftStation
{
  public const int Press = 1002;
  public const int OutputSlot = 4;
  public const int ByproductSlot = 5;

  public static bool Refuses;
  public static bool Hands;
  public static bool MakesWrong;
  public static readonly List<string> Pressed = [];

  protected override MachineSlotSpec[] SlotSpecs =>
    [
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.Input(s =>
        s?.Collectible.Code.Path.StartsWith("rod-") == true
      ),
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.Output(),
      MachineSlotSpec.Output(),
    ];

  public override string InventoryClassName => "craft-fixture-bench";

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    this.DisablePickRangeCheck();
  }

  public IReadOnlyList<CraftOrder> Orders(IWorldAccessor world) =>
    [
      .. world
        .GridRecipes.Where(r =>
          r.Width <= 2 && r.Height <= 2 && r.Output?.ResolvedItemStack != null
        )
        .Select(r => new CraftOrder(
          r.Name?.ToString(),
          [
            .. r.ResolvedIngredients!.Select(
                (ingredient, i) =>
                  ingredient == null
                    ? null
                    : new CraftInput(
                      (i / r.Width * 2) + (i % r.Width),
                      ingredient.Id,
                      ingredient
                    )
              )
              .OfType<CraftInput>(),
          ],
          Press,
          null,
          [OutputSlot, ByproductSlot],
          r.Output!.ResolvedItemStack.Clone(),
          r
        )),
    ];

  private CraftSequenceRun? _run;
  private float _used;

  public void StartHold() => _used = 0f;

  public bool Work(IWorldAccessor world, IPlayer player, float used)
  {
    float dt = used - _used;
    _used = used;
    CraftOrder order = Orders(world)
      .First(o => CraftSequenceRegistry.Shared.Sequence(o.Recipe) != null);
    _run ??= new CraftSequenceRun(
      CraftSequenceRegistry.Shared.Sequence(order.Recipe)!,
      order,
      [.. Inventory],
      new Random(1)
    );
    return _run.Hold(
        world,
        dt,
        player.Entity.RightHandItemSlot,
        player.Entity.LeftHandItemSlot,
        false
      ) == CraftSequenceProgress.Working;
  }

  protected override bool OnStationPacket(
    IPlayer player,
    int packetid,
    byte[] data
  )
  {
    if (packetid != Press)
      return false;
    ItemSlot[] grid = [Inventory[0], Inventory[1], Inventory[2], Inventory[3]];
    Pressed.Add(
      string.Join(
        ",",
        grid.Select(s =>
          s.Empty ? "-" : s.Itemstack.Collectible.Code.ToString()
        )
      )
    );
    if (Refuses)
      return true;
    if (MakesWrong)
    {
      Inventory[OutputSlot].Itemstack = new ItemStack(
        Api.World.GetItem(new AssetLocation("setupcraft:plate")),
        1
      );
      return true;
    }
    GridRecipe? recipe = Api.World.GridRecipes.FirstOrDefault(r =>
      r.Width <= 2 && r.Height <= 2 && r.Matches(player, Api.World, grid, 2)
    );
    if (recipe == null)
      return true;
    recipe.GenerateOutputStack(grid, Inventory[OutputSlot]);
    recipe.ConsumeInput(player, grid, 2);
    if (Hands)
    {
      player.InventoryManager.TryGiveItemstack(Inventory[OutputSlot].Itemstack);
      Inventory[OutputSlot].Itemstack = null;
    }
    return true;
  }
}

internal sealed class CraftFixtureBenchBlock : Block
{
  public static bool Deaf;

  private static CraftFixtureBench? Bench(
    IWorldAccessor world,
    BlockSelection selection
  ) =>
    world.BlockAccessor.GetBlockEntity(selection.Position) as CraftFixtureBench;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (Deaf)
      return false;
    Bench(world, blockSel)?.StartHold();
    return true;
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) => Bench(world, blockSel)?.Work(world, byPlayer, secondsUsed) ?? false;
}

internal sealed class CraftFixtureChest : BlockEntity { }

/// <summary>The <c>craft</c> step in the harness: the hand grid and a station's window, the
/// recipes the step loads, and what fails it (setups.md, <c>craft</c>).</summary>
[Collection("Setups")]
public class CraftStepTests : IDisposable
{
  private const string Straight = "platedpipes:pipe-plated-straight-ns";
  private const string Frame = "setupcraft:frame";

  private static readonly string PlatedPath = Path.Combine(
    RepoPaths.Root,
    "samples",
    "PlatedPipes"
  );

  private static readonly string BenchPath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupCraft"
  );

  private const string Bench =
    "{\"place\": {\"id\": \"bench\", \"code\": \"setupcraft:bench\", \"at\": [0, 0, 0]}}";

  public CraftStepTests()
  {
    CraftFixtureBench.Refuses = false;
    CraftFixtureBench.MakesWrong = false;
    CraftFixtureBench.Hands = false;
    CraftFixtureBench.Pressed.Clear();
    CraftFixtureBenchBlock.Deaf = false;
    CraftSequenceRegistry.Shared.Clear();
  }

  public void Dispose()
  {
    CraftFixtureBench.Refuses = false;
    CraftFixtureBench.MakesWrong = false;
    CraftFixtureBench.Hands = false;
    CraftFixtureBenchBlock.Deaf = false;
    CraftSequenceRegistry.Shared.Clear();
    SetupPlacers.Clear();
  }

  private static SetupScene Build(string mod, string path, string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\""
          + mod
          + "\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [path],
      null,
      Register
    );

  private static void Register(TestWorld w) =>
    w.RegisterClass("CraftFixtureBench", typeof(CraftFixtureBench))
      .RegisterClass("CraftFixtureBenchBlock", typeof(CraftFixtureBenchBlock))
      .RegisterBlockEntityFactory(
        "CraftFixtureBench",
        () => new CraftFixtureBench()
      )
      .RegisterClass("CraftFixtureChest", typeof(CraftFixtureChest))
      .RegisterBlockEntityFactory(
        "CraftFixtureChest",
        () => new CraftFixtureChest()
      );

  private static SetupScene AtBench(string craft) =>
    Build("setupcraft", BenchPath, "[" + Bench + ", " + craft + "]");

  private static string Refused(string mod, string path, string steps) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(mod, path, steps))
      .Message;

  private static string BenchRefused(string craft) =>
    Refused("setupcraft", BenchPath, "[" + Bench + ", " + craft + "]");

  private static int Carried(SetupScene scene, string code) =>
    scene
      .Player.Hotbar.Where(s =>
        !s.Empty && s.Itemstack.Collectible.Code.ToString() == code
      )
      .Sum(s => s.StackSize);

  // Fails when ConsumeInput is skipped.
  [Fact]
  public void Craft_in_the_hand_grid_makes_the_plated_straight()
  {
    using SetupScene scene = Build(
      "platedpipes",
      PlatedPath,
      "[{\"craft\": {\"output\": \"" + Straight + "\"}}]"
    );

    Assert.Equal(2, Carried(scene, Straight));
    Assert.NotEmpty(scene.HandGridSlots);
    Assert.All(scene.HandGridSlots, slot => Assert.True(slot.Empty));
  }

  // Fails when the LoadRecipes call is removed.
  [Fact]
  public void A_setup_file_with_a_craft_step_loads_the_recipes()
  {
    using SetupScene scene = Build(
      "platedpipes",
      PlatedPath,
      "[{\"craft\": {\"output\": \"" + Straight + "\"}}]"
    );

    Assert.Contains(
      scene.World.World.GridRecipes,
      r => r.Output.Code.ToString() == Straight
    );
  }

  // Fails when the codes the recipes name are not added to the vanilla codes.
  [Fact]
  public void A_setup_loads_the_vanilla_codes_a_recipe_names()
  {
    using SetupScene scene = Build(
      "platedpipes",
      PlatedPath,
      "[{\"craft\": {\"output\": \"" + Straight + "\"}}]"
    );

    Assert.NotNull(
      scene.World.World.GetItem(new AssetLocation("game:metalplate-iron"))
    );
  }

  // Fails when the press id is not the order's.
  [Fact]
  public void Craft_at_a_station_presses_its_order()
  {
    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );

    Assert.Equal(3, Carried(scene, Frame));
    Assert.Single(CraftFixtureBench.Pressed);
  }

  // Fails when the product check goes.
  [Fact]
  public void Craft_fails_on_a_product_the_order_does_not_make()
  {
    CraftFixtureBench.MakesWrong = true;

    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );

    Assert.Contains("made 1 setupcraft:plate, not 3 " + Frame, message);
  }

  // Fails when a press that yields nothing passes.
  [Fact]
  public void Craft_fails_when_the_station_refuses_the_press()
  {
    CraftFixtureBench.Refuses = true;

    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );

    Assert.Contains("yielded nothing: the station refused", message);
  }

  // Fails when CraftAt counts only the output slots, not what the station handed the player.
  [Fact]
  public void Craft_counts_a_product_the_station_hands_to_the_player()
  {
    CraftFixtureBench.Hands = true;

    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );

    Assert.Equal(3, Carried(scene, Frame));
  }

  // Fails when HandedOver leaves the grown stack in the slot, so the runner's Stow counts it twice.
  [Fact]
  public void A_handed_over_stack_leaves_the_slot_and_a_merge_counts_only_its_growth()
  {
    using SetupScene scene = Build("setupcraft", BenchPath, "[]");
    Item frame = scene.World.World.GetItem(new AssetLocation(Frame));
    var held = new DummySlot(new ItemStack(frame, 2));
    var fresh = new DummySlot();
    var before = CraftBills.Carried([held, fresh]);

    held.Itemstack.StackSize = 5;
    fresh.Itemstack = new ItemStack(frame, 1);
    List<ItemStack> got = CraftBills.HandedOver(before, [held, fresh]);

    Assert.Equal([3, 1], got.Select(s => s.StackSize));
    Assert.Equal(2, held.StackSize);
    Assert.True(fresh.Empty);
  }

  // Fails when the failure line drops the names.
  [Fact]
  public void Craft_names_the_stations_that_make_it()
  {
    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:wide\"}}"
    );

    Assert.Contains("has no order that makes setupcraft:wide", message);
    Assert.Contains("made at: the hand grid", message);
  }

  // Fails when a product no loaded station makes names a station.
  [Fact]
  public void Craft_says_when_no_loaded_station_makes_it()
  {
    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:plate\"}}"
    );

    Assert.Contains("made at: no loaded station", message);
  }

  // Fails when HandGrid.Orders drops its width check, its height check, or lays a recipe in
  // its own width.
  [Fact]
  public void HandGrid_orders_leave_out_what_the_grid_cannot_hold()
  {
    using var world = new TestWorld();
    Register(world);
    world.LoadAssets([BenchPath], null, []);
    world.LoadRecipes();

    Assert.Contains(
      world.World.GridRecipes,
      r => r.Width == 5 && r.Height == 5
    );
    Assert.Contains(
      world.World.GridRecipes,
      r => r.Width == 4 && r.Height == 1
    );
    Assert.Contains(
      world.World.GridRecipes,
      r => r.Width == 1 && r.Height == 4
    );
    IReadOnlyList<CraftOrder> orders = HandGrid.Orders(world.World);
    string[] made =
    [
      .. orders.Select(o => o.Product.Collectible.Code.ToString()),
    ];
    Assert.Contains(Frame, made);
    Assert.DoesNotContain("setupcraft:member", made);
    Assert.DoesNotContain("setupcraft:long", made);
    Assert.DoesNotContain("setupcraft:tall", made);
    Assert.Equal(
      [0, 1, 3, 4],
      orders.First(o => o.Recipe == Frame).Inputs.Select(i => i.Slot)
    );
  }

  // Fails when a shapeless recipe is laid by its pattern's cells.
  [Fact]
  public void HandGrid_lays_a_shapeless_recipe_from_slot_zero()
  {
    using var world = new TestWorld();
    Register(world);
    world.LoadAssets([BenchPath], null, []);
    world.LoadRecipes();

    CraftOrder mix = HandGrid
      .Orders(world.World)
      .Single(o => o.Product.Collectible.Code.ToString() == "setupcraft:mix");

    Assert.Equal([0, 1], mix.Inputs.Select(i => i.Slot));
  }

  // Fails when with is ignored.
  [Fact]
  public void Craft_with_picks_the_wildcard_variant()
  {
    using SetupScene picked = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\", \"with\": {\"R\": \"setupcraft:rod-iron\"}}}"
    );
    string iron = Assert.Single(CraftFixtureBench.Pressed);
    CraftFixtureBench.Pressed.Clear();
    using SetupScene first = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );

    Assert.Equal(
      "setupcraft:rod-iron,setupcraft:rod-iron,setupcraft:plate,setupcraft:plate",
      iron
    );
    Assert.Equal(
      "setupcraft:rod-copper,setupcraft:rod-copper,setupcraft:plate,setupcraft:plate",
      Assert.Single(CraftFixtureBench.Pressed)
    );
  }

  // Fails when HandGrid.Orders leaves the ingredient's name off its inputs.
  [Fact]
  public void Craft_with_a_code_the_hand_grid_slot_does_not_take_fails()
  {
    string message = Refused(
      "setupcraft",
      BenchPath,
      "[{\"craft\": {\"output\": \""
        + Frame
        + "\", \"with\": {\"R\": \"setupcraft:plate\"}}}]"
    );

    Assert.Contains(
      "with R: setupcraft:plate is not an ingredient the slot takes",
      message
    );
  }

  // Fails when with takes a code the ingredient does not fit.
  [Fact]
  public void Craft_with_a_code_the_slot_does_not_take_fails()
  {
    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\", \"with\": {\"R\": \"setupcraft:plate\"}}}"
    );

    Assert.Contains(
      "with R: setupcraft:plate is not an ingredient the slot takes",
      message
    );
  }

  // Fails when with takes a code the load did not register.
  [Fact]
  public void Craft_with_an_unregistered_code_fails()
  {
    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\", \"with\": {\"R\": \"setupcraft:rod-gold\"}}}"
    );

    Assert.Contains(
      "\"setupcraft:rod-gold\" is not registered by the load",
      message
    );
  }

  // Fails when with takes a key that is no input of the order.
  [Fact]
  public void Craft_with_a_key_that_is_no_input_of_the_order_fails()
  {
    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\", \"with\": {\"X\": \"setupcraft:rod-iron\"}}}"
    );

    Assert.Contains("with X is no input of setupcraft:frame", message);
    Assert.Contains("its inputs are", message);
  }

  // Fails when the hand grid's craft leaves the key unchecked.
  [Fact]
  public void Craft_in_the_hand_grid_with_a_key_that_is_no_input_fails()
  {
    string message = Refused(
      "setupcraft",
      BenchPath,
      "[{\"craft\": {\"output\": \""
        + Frame
        + "\", \"with\": {\"X\": \"setupcraft:rod-iron\"}}}]"
    );

    Assert.Contains("with X is no input of setupcraft:frame", message);
  }

  // Fails when LoadRecipes leaves the RecipeRegistrySystem unregistered or registers nothing in it.
  [Fact]
  public void The_game_api_answers_the_smithing_and_clay_forming_recipes_the_load_kept()
  {
    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );
    TestWorld world = scene.World;

    Assert.NotEmpty(world.SmithingRecipes);
    Assert.Equal(world.SmithingRecipes, world.Api.GetSmithingRecipes());
    Assert.Equal(
      "setupcraft:pin",
      Assert.Single(world.Api.GetSmithingRecipes()).Output.Code.ToString()
    );
    Assert.Equal(
      "setupcraft:stud",
      Assert.Single(world.Api.GetClayformingRecipes()).Output.Code.ToString()
    );
  }

  // Fails when the registry's warning about a smithing recipe with no code reaches the world's log.
  [Fact]
  public void Registering_a_smithing_recipe_with_no_code_logs_nothing()
  {
    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\"}}"
    );

    Assert.DoesNotContain(
      scene.World.Log.Entries,
      e => e.Message.Contains("unchanging code", StringComparison.Ordinal)
    );
  }

  // Fails when times is ignored.
  [Fact]
  public void Craft_times_multiplies_the_product()
  {
    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\", \"times\": 2}}"
    );

    Assert.Equal(6, Carried(scene, Frame));
    Assert.Equal(2, CraftFixtureBench.Pressed.Count);
  }

  // Fails when recipe is ignored.
  [Fact]
  public void Craft_recipe_picks_the_named_order()
  {
    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \""
        + Frame
        + "\", \"recipe\": \"setupcraft:frame-alt\"}}"
    );

    Assert.Equal(
      "setupcraft:plate,setupcraft:rod-copper,setupcraft:plate,setupcraft:rod-copper",
      Assert.Single(CraftFixtureBench.Pressed)
    );
  }

  // Fails when a slot that takes none of the laid stack passes.
  [Fact]
  public void Craft_names_the_slot_that_refuses_an_input()
  {
    string message = BenchRefused(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:knob\"}}"
    );

    Assert.Contains("slot 1 of setupcraft:bench", message);
    Assert.Contains("took 0 of the 1 setupcraft:stud", message);
  }

  // Fails when the tool is left in its slot.
  [Fact]
  public void Craft_takes_the_tool_back_out_of_the_station()
  {
    using SetupScene scene = AtBench(
      "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"setupcraft:pin\"}}"
    );

    var bench = Assert.IsType<CraftFixtureBench>(
      scene.World.GetBlockEntity(scene.PositionOf("bench"))
    );
    Assert.Equal(1, Carried(scene, "setupcraft:pin"));
    Assert.True(bench.Inventory[0].Empty);
    Assert.Equal(0, Carried(scene, "setupcraft:mallet"));
  }

  // Fails when a block entity that is no station passes.
  [Fact]
  public void Craft_at_a_block_that_is_no_station_fails()
  {
    string message = Refused(
      "setupcraft",
      BenchPath,
      "[{\"place\": {\"id\": \"chest\", \"code\": \"setupcraft:chest\", \"at\": [0, 0, 0]}}, "
        + "{\"craft\": {\"at\": {\"of\": \"chest\"}, \"output\": \""
        + Frame
        + "\"}}]"
    );

    Assert.Contains("is not a craft station", message);
  }

  // Fails when a times below one is accepted.
  [Fact]
  public void Craft_times_below_one_is_refused()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        SetupFile.Parse(
          "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupcraft\"], \"steps\": "
            + "[{\"craft\": {\"output\": \""
            + Frame
            + "\", \"times\": 0}}], \"seconds\": 10, \"record\": {}}",
          "t.json"
        )
      )
      .Message;

    Assert.Contains("takes a whole number of at least 1", message);
  }

  private const string Hammered =
    "{\"machine\": \"bench\", \"sequences\": [{\"recipe\": \"setupcraft:frame\", \"steps\": ["
    + "{\"tool\": \"setupcraft:mallet\", \"seconds\": 1}, "
    + "{\"tool\": \"setupcraft:mallet\", \"offhand\": \"setupcraft:stud\", \"seconds\": 1}]}]}";

  private const string SequencedBench =
    "{\"place\": {\"id\": \"bench\", \"code\": \"setupcraft:seqbench\", \"at\": [0, 0, 0]}}";

  private const string FrameCraft =
    "{\"craft\": {\"at\": {\"of\": \"bench\"}, \"output\": \"" + Frame + "\"}}";

  private static string PackOf(string gathered, string ends = "") =>
    "\"pack\": {\"scope\": \"stacks\", \"gathered\": {"
    + "\"setupcraft:rod-copper\": 2, \"setupcraft:plate\": 2"
    + gathered
    + "}"
    + (ends == "" ? "" : ", \"ends\": {\"stacks\": {" + ends + "}}")
    + "}, ";

  private static SetupFile Sequenced(string pack) =>
    SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupcraft\"], "
        + pack
        + "\"steps\": ["
        + SequencedBench
        + ", "
        + FrameCraft
        + "], \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

  private static SetupScene BuildSequenced(
    string pack,
    string sequences = Hammered
  ) =>
    SetupScene.Build(
      Sequenced(pack),
      [BenchPath],
      _ =>
        CraftSequenceLoader.Load([
          ("setupcraft:config/craftsequences/bench.json", sequences),
        ]),
      Register
    );

  private const string Tools =
    ", \"setupcraft:mallet\": 1, \"setupcraft:stud\": 1";

  // Fails when the craft presses the order's button instead of holding the sequence's steps.
  [Fact]
  public void Craft_of_a_sequenced_recipe_holds_each_step_instead_of_pressing()
  {
    using SetupScene scene = BuildSequenced(PackOf(Tools));

    Assert.Empty(CraftFixtureBench.Pressed);
    Assert.Equal(3, Carried(scene, Frame));
  }

  // Fails when the harness holds a hits step for its seconds (none) instead of its swings.
  [Fact]
  public void Craft_of_a_sequenced_recipe_holds_a_hits_step_for_its_swings()
  {
    using SetupScene scene = BuildSequenced(
      PackOf(Tools),
      "{\"machine\": \"bench\", \"sequences\": [{\"recipe\": \"setupcraft:frame\", \"steps\": ["
        + "{\"tool\": \"setupcraft:mallet\", \"hits\": 2}, "
        + "{\"tool\": \"setupcraft:mallet\", \"offhand\": \"setupcraft:stud\", \"hits\": 1}]}]}"
    );

    Assert.Equal(3, Carried(scene, Frame));
  }

  // Fails when the tools are made without the pack, or the pack is not asked for them.
  [Fact]
  public void Craft_of_a_sequenced_recipe_needs_its_tool_in_the_pack()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        BuildSequenced(PackOf(", \"setupcraft:stud\": 1"))
      )
      .Message;

    Assert.Contains("step 1 of setupcraft:frame", message);
    Assert.Contains("the pack holds no setupcraft:mallet", message);
  }

  // Fails when the off hand is not laid for a step that names it.
  [Fact]
  public void Craft_of_a_sequenced_recipe_names_a_missing_offhand_tool()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        BuildSequenced(PackOf(", \"setupcraft:mallet\": 1"))
      )
      .Message;

    Assert.Contains("step 2 of setupcraft:frame", message);
    Assert.Contains("the pack holds no setupcraft:stud", message);
  }

  // Fails when a step's tools are not given back to the pack once the step is held.
  [Fact]
  public void Craft_of_a_sequenced_recipe_gives_the_tools_back_to_the_pack()
  {
    using SetupScene scene = BuildSequenced(
      PackOf(Tools, "\"setupcraft:mallet\": 1, \"setupcraft:stud\": 1")
    );

    Assert.Equal(3, Carried(scene, Frame));
  }

  private static string Shedding(string code) =>
    Hammered.Replace(
      "]}]}",
      "],"
        + "\"byproducts\": [{\"type\": \"item\", \"code\": \""
        + code
        + "\", \"quantity\": 1}]}]}"
    );

  // Fails when the check counts only the product, or the byproducts are not given to the pack.
  [Fact]
  public void Craft_of_a_sequenced_recipe_takes_its_byproducts_and_gives_them_to_the_pack()
  {
    using SetupScene scene = BuildSequenced(
      PackOf(Tools, "\"setupcraft:pin\": 1"),
      Shedding("setupcraft:pin")
    );

    Assert.Equal(3, Carried(scene, Frame));
    Assert.Equal(1, Carried(scene, "setupcraft:pin"));
  }

  // Fails when a step's tool is not drawn from the pack, so one tool serves both hands.
  [Fact]
  public void Craft_of_a_sequenced_recipe_draws_a_tool_for_each_hand()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        BuildSequenced(
          PackOf(", \"setupcraft:mallet\": 1"),
          Hammered.Replace("setupcraft:stud", "setupcraft:mallet")
        )
      )
      .Message;

    Assert.Contains("step 2 of setupcraft:frame", message);
    Assert.Contains("the pack holds no setupcraft:mallet", message);
  }

  // Fails when a case without a pack gets no tool made for the step.
  [Fact]
  public void Craft_of_a_sequenced_recipe_makes_its_tools_without_a_pack()
  {
    using SetupScene scene = BuildSequenced("");

    Assert.Empty(CraftFixtureBench.Pressed);
    Assert.Equal(3, Carried(scene, Frame));
  }

  // Fails when the answer of the block to the first click is ignored.
  [Fact]
  public void Craft_of_a_sequenced_recipe_fails_when_the_block_takes_no_hold()
  {
    CraftFixtureBenchBlock.Deaf = true;

    string message = Assert
      .Throws<InvalidOperationException>(() => BuildSequenced(""))
      .Message;

    Assert.Contains(
      "does not take the hold of step 1 of setupcraft:frame",
      message
    );
  }

  // Fails when a pack key that matches a tool but names nothing loaded is passed over.
  [Fact]
  public void Craft_of_a_sequenced_recipe_names_a_pack_tool_the_load_did_not_register()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        BuildSequenced(
          PackOf(", \"setupcraft:ghost\": 1"),
          Hammered.Replace("setupcraft:mallet", "setupcraft:ghost")
        )
      )
      .Message;

    Assert.Contains(
      "setupcraft:ghost, which the load did not register",
      message
    );
  }
}
#endif
