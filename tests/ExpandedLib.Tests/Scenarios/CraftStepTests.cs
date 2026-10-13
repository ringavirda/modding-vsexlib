#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

internal sealed class CraftFixtureBench
  : BlockEntityMachineStation,
    ICraftStation
{
  public const int Press = 1002;
  public const int OutputSlot = 4;

  public static bool Refuses;
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
          [OutputSlot],
          r.Output!.ResolvedItemStack.Clone(),
          r
        )),
    ];

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
    return true;
  }
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
    CraftFixtureBench.Pressed.Clear();
  }

  public void Dispose()
  {
    CraftFixtureBench.Refuses = false;
    CraftFixtureBench.MakesWrong = false;
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
}
#endif
