#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Checks;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

// Crafts the recipes of a 1 x 1 input a press hands to the output slot, refusing the recipe in
// Refused.
internal sealed class WalkFixtureBench
  : BlockEntityMachineStation,
    ICraftStation
{
  public const int Press = 1002;

  public static string? Refused;
  public static readonly List<string> Laid = [];

  protected override MachineSlotSpec[] SlotSpecs =>
    [
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.Output(),
      MachineSlotSpec.Diagram(),
    ];

  public override string InventoryClassName => "walk-fixture-bench";

  private ExRightClickConstructable? _rcc;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    this.DisablePickRangeCheck();
  }

  public IReadOnlyList<CraftOrder> Orders(IWorldAccessor world) =>
    PressOrders(world);

  internal static IReadOnlyList<CraftOrder> PressOrders(IWorldAccessor world) =>
    [
      .. world
        .GridRecipes.Where(r =>
          r.Width > HandGrid.Size
          || RecipeDiagram.Of(r) != null
          || CraftSequenceRegistry.Shared.Sequence(r.Name.ToString()) != null
        )
        .Select(r => new CraftOrder(
          r.Name.ToString(),
          [
            .. r.ResolvedIngredients!.OfType<CraftingRecipeIngredient>()
              .Select(
                (ingredient, i) => new CraftInput(0, ingredient.Id, ingredient)
              ),
            .. RecipeDiagram.Ingredient(r, world) is { } diagram
              ? [new CraftInput(2, "diagram", diagram)]
              : Array.Empty<CraftInput>(),
          ],
          Press,
          Encoding.UTF8.GetBytes(r.Name.ToString()),
          [1],
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
    if (
      packetid != Press
      || (_rcc ??= GetBehavior<ExRightClickConstructable>())
        is { IsComplete: false }
    )
      return false;
    Laid.Add(Inventory[0].Itemstack.Collectible.Code.ToString());
    string name = Encoding.UTF8.GetString(data);
    Inventory[0].Itemstack = null;
    if (name == Refused)
      return true;
    Inventory[1].Itemstack = Api
      .World.GridRecipes.First(r => r.Name.ToString() == name)
      .Output.ResolvedItemStack.Clone();
    return true;
  }
}

internal sealed class WalkFixtureBenchBlock : Block
{
  private static WalkFixtureBench? Bench(
    IWorldAccessor world,
    BlockSelection selection
  ) =>
    world.BlockAccessor.GetBlockEntity(selection.Position) as WalkFixtureBench;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
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

// Crafts the 4 x 1 recipes through the game's matcher over four input slots and one output, so a
// foreign stack left in an input slot refuses the press.
internal sealed class WalkFixtureTrayBench
  : BlockEntityMachineStation,
    ICraftStation
{
  public const int Width = 4;

  protected override MachineSlotSpec[] SlotSpecs =>
    [
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.AnyInput(),
      MachineSlotSpec.Output(),
    ];

  public override string InventoryClassName => "walk-fixture-tray-bench";

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    this.DisablePickRangeCheck();
  }

  public IReadOnlyList<CraftOrder> Orders(IWorldAccessor world) =>
    [
      .. world
        .GridRecipes.Where(r => r.Width == Width)
        .Select(r => new CraftOrder(
          r.Name.ToString(),
          [
            .. r.ResolvedIngredients!.Select(
                (ingredient, i) =>
                  ingredient == null
                    ? null
                    : new CraftInput(i, ingredient.Id, ingredient)
              )
              .OfType<CraftInput>(),
          ],
          WalkFixtureBench.Press,
          Encoding.UTF8.GetBytes(r.Name.ToString()),
          [Width],
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
    if (packetid != WalkFixtureBench.Press)
      return false;
    ItemSlot[] grid = [Inventory[0], Inventory[1], Inventory[2], Inventory[3]];
    string name = Encoding.UTF8.GetString(data);
    GridRecipe recipe = Api.World.GridRecipes.First(r =>
      r.Name.ToString() == name
    );
    if (!recipe.Matches(player, Api.World, grid, Width))
      return true;
    recipe.GenerateOutputStack(grid, Inventory[Width]);
    recipe.ConsumeInput(player, grid, Width);
    return true;
  }
}

// A WalkFixtureBench that reads its construction through the behaviour on every press and keeps no
// _rcc or _animator field.
internal sealed class WalkFixtureUncachedBench
  : BlockEntityMachineStation,
    ICraftStation
{
  protected override MachineSlotSpec[] SlotSpecs =>
    [MachineSlotSpec.AnyInput(), MachineSlotSpec.Output()];

  public override string InventoryClassName => "walk-fixture-uncached-bench";

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    this.DisablePickRangeCheck();
  }

  public IReadOnlyList<CraftOrder> Orders(IWorldAccessor world) =>
    WalkFixtureBench.PressOrders(world);

  protected override bool OnStationPacket(
    IPlayer player,
    int packetid,
    byte[] data
  )
  {
    if (
      packetid != WalkFixtureBench.Press
      || GetBehavior<ExRightClickConstructable>() is { IsComplete: false }
    )
      return false;
    string name = Encoding.UTF8.GetString(data);
    Inventory[0].Itemstack = null;
    Inventory[1].Itemstack = Api
      .World.GridRecipes.First(r => r.Name.ToString() == name)
      .Output.ResolvedItemStack.Clone();
    return true;
  }
}

/// <summary><see cref="CraftWalk"/> over a planted domain <c>walk</c> whose recipes chain from
/// vanilla codes.</summary>
public sealed class CraftWalkTests(ITestOutputHelper output) : IDisposable
{
  private readonly PlantedFiles _files = new();

  public void Dispose()
  {
    WalkFixtureBench.Refused = null;
    WalkFixtureBench.Laid.Clear();
    _files.Dispose();
    ExlibChecks.ClearDeclarations();
    ProcessJobRegistry.Shared.Clear();
    ProcessRouteRegistry.Shared.Clear();
    CraftSequenceRegistry.Shared.Clear();
  }

  private static string Item(string code) =>
    $"{{ \"code\": \"{code}\", \"creativeinventory\": {{ \"general\": [\"*\"] }} }}";

  // A code with no creative tab is not a kind, so the edges waiting on it are the findings.
  private static string Plain(string code) => $"{{ \"code\": \"{code}\" }}";

  private static string Recipe(
    string input,
    string output,
    string type = "item"
  ) =>
    $$"""
      {
        "name": "walk:{{output}}",
        "ingredientPattern": "A",
        "width": 1,
        "height": 1,
        "ingredients": { "A": { "type": "item", "code": "{{input}}" } },
        "output": { "type": "{{type}}", "code": "walk:{{output}}" }
      }
      """;

  private TestWorld Planted(
    string[] items,
    string[] recipes,
    params string[] vanilla
  )
  {
    _files.Write(
      "modinfo.json",
      "{ \"type\": \"content\", \"modid\": \"walk\", \"name\": \"Walk\", \"version\": \"1.0.0\" }"
    );
    foreach (string item in items)
      _files.Write($"assets/walk/itemtypes/{item}.json", Item(item));
    _files.Write(
      "assets/walk/recipes/grid/walk.json",
      "[" + string.Join(",", recipes) + "]"
    );
    var world = new TestWorld();
    world
      .RegisterClass("WalkFixtureBench", typeof(WalkFixtureBench))
      .RegisterClass("WalkFixtureBenchBlock", typeof(WalkFixtureBenchBlock))
      .RegisterClass("WalkFixtureTrayBench", typeof(WalkFixtureTrayBench))
      .RegisterClass(
        "WalkFixtureUncachedBench",
        typeof(WalkFixtureUncachedBench)
      )
      .RegisterClass(
        "ExRightClickConstructable",
        typeof(ExRightClickConstructable)
      );
    world.LoadAssets([_files.Root], null, vanilla);
    world.LoadRecipes();
    return world;
  }

  private CraftWalk.Result Walk(
    TestWorld world,
    string? kit = null,
    string? known = null,
    System.Func<string, bool>? owns = null
  )
  {
    CraftWalk.Result result = CraftWalk.Run(
      world,
      "walk",
      kit == null ? null : _files.Write("kit.json", kit),
      known == null ? null : _files.Write("known.json", known),
      owns
    );
    output.WriteLine(result.ToString());
    return result;
  }

  private const string Stick = "game:stick";

  [Fact]
  public void A_chain_from_a_gathered_code_passes()
  {
    using TestWorld world = Planted(
      ["a", "b", "c"],
      [Recipe(Stick, "a"), Recipe("walk:a", "b"), Recipe("walk:b", "c")],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    Assert.Equal(
      "craft walk walk: 3 kinds, 3 recipes fired (3 executed, 0 followed), 0 undeclared, 0 on trust, 0 failures",
      result.Summary
    );
  }

  // Fails when Run drops a stage from Steps or leaves its seconds unmeasured.
  [Fact]
  public void The_walk_reports_the_seconds_of_each_stage()
  {
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(world);

    Assert.Equal(
      ["read edges", "reach", "execute", "check"],
      result.Steps.Select(s => s.Step)
    );
    Assert.Contains(result.Steps, s => s.Seconds > 0);
    Assert.Equal(4, result.Timings().Split(Environment.NewLine).Length);
  }

  [Fact]
  [PlantedDefect(typeof(CraftWalk), nameof(CraftWalk.Run))]
  public void Dropping_the_middle_recipe_fails_naming_its_output()
  {
    using TestWorld world = Planted(
      ["a", "b", "c"],
      [Recipe(Stick, "a"), Recipe("walk:b", "c")],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    string finding = Assert.Single(result.Findings);
    Assert.StartsWith("walk:b | ", finding);
    Assert.Contains("no recipe makes it", finding);
    Assert.Contains("1 failures", result.Summary);
  }

  [Fact]
  public void A_game_input_in_neither_list_fails_naming_it_and_the_kit_clears_it()
  {
    using TestWorld world = Planted(
      ["a"],
      [Recipe("game:ingot-iron", "a")],
      "game:ingot-*"
    );

    CraftWalk.Result without = Walk(world);
    CraftWalk.Result with = Walk(
      world,
      kit: "{ \"game:ingot-iron\": { \"route\": \"smelted from ore\" } }"
    );

    string finding = Assert.Single(without.Findings);
    Assert.StartsWith("game:ingot-iron | asked for by walk:a", finding);
    Assert.Empty(with.Findings);
  }

  [Fact]
  public void A_kit_row_matching_nothing_fails()
  {
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(
      world,
      kit: "{ \"game:ingot-iron\": \"smelted from ore\" }"
    );

    Assert.Equal(
      ["game:ingot-iron | kit row matches no loaded block or item"],
      result.Findings
    );
  }

  [Fact]
  public void A_known_row_whose_kind_passes_fails_as_stale()
  {
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(
      world,
      known: "{ \"walk:a\": { \"why\": \"unreached\", \"task\": \"CC9\" } }"
    );

    Assert.Equal(
      ["walk:a | known row matches no failure; remove it"],
      result.Findings
    );
  }

  [Fact]
  public void A_known_row_accepts_its_failure()
  {
    using TestWorld world = Planted(["a", "b"], [Recipe(Stick, "a")], Stick);

    CraftWalk.Result result = Walk(
      world,
      known: "{ \"walk:b\": \"not built yet\" }"
    );

    Assert.Empty(result.Findings);
    Assert.Contains("0 failures", result.Summary);
  }

  // Fails when Run ignores owns and every shard crafts every recipe, or when Shard gives a recipe to two shards or
  // to none.
  [Fact]
  [PlantedDefect(typeof(CraftWalk), nameof(CraftWalk.Shard))]
  public void The_shards_of_a_split_craft_every_recipe_once_between_them()
  {
    using TestWorld world = Planted(
      ["a", "b", "c", "d", "e", "f"],
      [
        Recipe(Stick, "a"),
        Recipe(Stick, "b"),
        Recipe(Stick, "c"),
        Recipe(Stick, "d"),
        Recipe(Stick, "e"),
        Recipe(Stick, "f"),
      ],
      Stick
    );

    string[] whole = [.. Walk(world).Crafted.Select(c => c.Output).Order()];
    string[][] shards =
    [
      .. Enumerable
        .Range(0, 3)
        .Select(k =>
          Walk(world, owns: CraftWalk.Shard(k, 3))
            .Crafted.Select(c => c.Output)
            .Order()
            .ToArray()
        ),
    ];

    Assert.Equal(6, whole.Length);
    Assert.Equal(whole, shards.SelectMany(s => s).Order());
    Assert.True(shards.Count(s => s.Length > 0) > 1);
  }

  // Fails when a shard judges a known row of a product it does not craft: the stale row is then
  // reported by every shard instead of the one that owns the product.
  [Fact]
  public void A_stale_known_row_is_reported_by_the_shard_that_owns_its_product_only()
  {
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);

    string[] findings =
    [
      .. Enumerable
        .Range(0, 3)
        .SelectMany(k =>
          Walk(
            world,
            known: "{ \"walk:a\": { \"why\": \"unreached\", \"task\": \"CC9\" } }",
            owns: CraftWalk.Shard(k, 3)
          ).Findings
        ),
    ];

    Assert.Equal(
      ["walk:a | known row matches no failure; remove it"],
      findings
    );
  }

  private static void Sequence(string recipe, string tool) =>
    CraftSequenceLoader.Load([
      (
        "walk:config/craftsequences/walk.json",
        $$"""
        {
          "machine": "bench",
          "sequences": [
            { "recipe": "{{recipe}}", "steps": [ { "tool": "{{tool}}", "seconds": 2 } ] }
          ]
        }
        """
      ),
    ]);

  // Fails when the gate lets the hand grid match a sequenced recipe, or the walk crafts a
  // sequenced recipe in the hand grid.
  [Fact]
  public void A_sequenced_recipe_is_crafted_at_its_station_and_the_hand_grid_refuses_it()
  {
    using TestWorld world = WithBench(["a"], [Recipe(Stick, "a")]);
    Sequence("walk:a", Stick);

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    CraftWalk.CraftedEdge crafted = Assert.Single(result.Crafted);
    Assert.NotEqual(CraftWalk.HandGridName, crafted.Station);
    Assert.Empty(WalkFixtureBench.Laid);
  }

  private static string Keyed(string input, string output, string diagram) =>
    Recipe(input, output)
      .Replace(
        "\"width\": 1,",
        $"\"attributes\": {{ \"diagram\": \"{diagram}\" }}, \"width\": 1,"
      );

  // Fails when the walk drops a keyed recipe's diagram from its edge's needs: the recipe then fires,
  // and crafts, with no way to reach the diagram; or when it stops counting the recipe station-only.
  [Fact]
  public void A_diagram_recipe_fires_once_its_diagram_is_reached()
  {
    string keyed = Keyed(Stick, "a", "walk:diagram-x");
    using (TestWorld unreached = WithBench(["a", "diagram-x"], [keyed]))
    {
      CraftWalk.Result result = Walk(unreached);

      Assert.Empty(result.Crafted);
      Assert.StartsWith("walk:diagram-x | ", Assert.Single(result.Findings));
    }

    using TestWorld reached = WithBench(
      ["a", "diagram-x"],
      [keyed, Recipe(Stick, "diagram-x")]
    );

    CraftWalk.Result walked = Walk(reached);

    Assert.Empty(walked.Findings);
    Assert.Contains(
      walked.Crafted,
      c => c.Output == "walk:a" && c.Station != CraftWalk.HandGridName
    );
  }

  // Fails when the walk presses the sequenced recipe's button instead of holding its steps.
  [Fact]
  public void A_station_that_takes_no_hold_fails_a_sequenced_recipe()
  {
    _files.Write(
      "assets/walk/blocktypes/bench.json",
      Bench.Replace("WalkFixtureBenchBlock", "Block")
    );
    using TestWorld world = Planted(["a"], [Recipe(Stick, "a")], Stick);
    Sequence("walk:a", Stick);

    CraftWalk.Result result = Walk(world);

    string finding = Assert.Single(result.Findings);
    Assert.StartsWith("walk:a | station refuses: ", finding);
    Assert.Contains("does not take the hold of step 1 of walk:a", finding);
  }

  // Fails when a sequence keyed on a name no loaded recipe has passes the walk.
  [Fact]
  public void A_sequence_naming_no_loaded_recipe_fails()
  {
    using TestWorld world = WithBench(["a"], [Recipe(Stick, "a")]);
    Sequence("walk:recipes/grid/walk", Stick);

    CraftWalk.Result result = Walk(world);

    Assert.Contains(
      "walk:recipes/grid/walk | the sequence of bench names no loaded grid recipe",
      result.Findings
    );
  }

  // Fails when a byproduct code that matches no loaded collectible passes the walk.
  [Fact]
  public void A_sequence_shedding_an_unloaded_code_fails()
  {
    using TestWorld world = WithBench(["a"], [Recipe(Stick, "a")]);
    world.Log.Expect(EnumLogType.Error, "walk:ghost");
    CraftSequenceLoader.Load([
      (
        "walk:config/craftsequences/walk.json",
        """
        {
          "machine": "bench",
          "sequences": [
            {
              "recipe": "walk:a",
              "steps": [ { "tool": "game:stick", "seconds": 2 } ],
              "byproducts": [ { "type": "item", "code": "walk:ghost", "quantity": 1 } ]
            }
          ]
        }
        """
      ),
    ]);

    CraftWalk.Result result = Walk(world);

    Assert.Contains(
      "walk:a | the sequence of bench sheds walk:ghost, which no loaded item or block is",
      result.Findings
    );
  }

  // Fails when the walk stops checking the tool a sequence step names.
  [Fact]
  public void A_step_naming_an_unreached_tool_fails_and_a_reached_one_passes()
  {
    using TestWorld world = WithBench(
      ["a"],
      [Recipe(Stick, "a")],
      "game:wrench-iron"
    );
    Sequence("walk:a", "game:wrench-*");

    CraftWalk.Result result = Walk(world);

    string finding = Assert.Single(result.Findings);
    Assert.Equal(
      "walk:a | walk:a step 1 names game:wrench-*, which nothing reached",
      finding
    );
  }

  private static string Sized(
    string output,
    int width,
    int height,
    string input = "walk:a"
  )
  {
    string[] rows =
    [
      .. Enumerable
        .Range(0, height)
        .Select(y => new string([
          .. Enumerable
            .Range(0, width)
            .Select(x => x == width / 2 && y == height / 2 ? 'A' : '_'),
        ])),
    ];
    return $$"""
      {
        "name": "walk:{{output}}",
        "ingredientPattern": "{{string.Join(",", rows)}}",
        "width": {{width}},
        "height": {{height}},
        "ingredients": { "A": { "type": "item", "code": "{{input}}" } },
        "output": { "type": "item", "code": "walk:{{output}}" }
      }
      """;
  }

  [Fact]
  public void A_recipe_wider_or_taller_than_the_hand_grid_is_a_grid_station_recipe()
  {
    using TestWorld world = Planted(
      ["a", "small", "long", "tall", "member"],
      [
        Sized("small", 3, 3),
        Sized("long", 4, 1),
        Sized("tall", 1, 4),
        Sized("member", 5, 5),
      ]
    );

    CraftWalkClass Class(string output) =>
      CraftWalkEdges.ClassOf(
        world.World.GridRecipes.Single(r =>
          r.Name.ToString() == "walk:" + output
        )
      );

    Assert.Equal(CraftWalkClass.HandGrid, Class("small"));
    Assert.Equal(CraftWalkClass.GridStation, Class("long"));
    Assert.Equal(CraftWalkClass.GridStation, Class("tall"));
    Assert.Equal(CraftWalkClass.GridStation, Class("member"));
  }

  private static string Tower(string stack) =>
    $$"""
      {
        "code": "tower",
        "creativeinventory": { "general": [ "*" ] },
        "entityBehaviors": [
          {
            "name": "ExRightClickConstructable",
            "properties": {
              "stages": [
                { "requireStacks": [ { "type": "item", "code": "{{stack}}", "quantity": 1 } ] }
              ]
            }
          }
        ]
      }
      """;

  [Fact]
  public void A_structure_is_reached_only_when_its_core_and_stage_stacks_are()
  {
    _files.Write("assets/walk/blocktypes/tower.json", Tower("walk:a"));
    _files.Write("assets/walk/itemtypes/a.json", Plain("a"));
    using TestWorld reached = Planted(
      [],
      [Recipe(Stick, "a"), Recipe(Stick, "tower", "block")],
      Stick
    );
    Assert.Empty(Walk(reached).Findings);

    using TestWorld noStack = Planted(
      [],
      [Recipe(Stick, "tower", "block")],
      Stick
    );
    Assert.Contains(
      Walk(noStack).Findings,
      f => f.StartsWith("walk:tower | ") && f.Contains("waits on walk:a,")
    );
  }

  [Fact]
  public void A_machine_product_waits_on_its_machine_block()
  {
    _files.Write("assets/walk/itemtypes/press.json", Plain("press"));
    using TestWorld reached = Planted(
      ["made"],
      [Recipe(Stick, "press")],
      Stick
    );
    ExlibChecks.Produces("walk", "walk:made", "the press", "walk:press", Stick);
    Assert.Empty(Walk(reached).Findings);

    using TestWorld unreached = Planted(["made"], [], Stick);
    ExlibChecks.Produces("walk", "walk:made", "the press", "walk:press", Stick);
    Assert.Contains(
      Walk(unreached).Findings,
      f => f.StartsWith("walk:made | ") && f.Contains("waits on walk:press,")
    );
  }

  [Fact]
  public void A_three_argument_declaration_is_undeclared_and_fires_nothing()
  {
    using TestWorld world = Planted(
      ["press", "made"],
      [Recipe(Stick, "press")],
      Stick
    );
    ExlibChecks.Produces("walk", "walk:made", "the press");

    CraftWalk.Result result = Walk(world);

    Assert.Contains("1 undeclared", result.Summary);
    Assert.Contains(
      result.Findings,
      f => f.StartsWith("walk:made | ") && f.Contains("no recipe makes it")
    );
  }

  [Fact]
  public void A_job_output_waits_on_the_block_runs_names()
  {
    _files.Write("assets/walk/itemtypes/shear.json", Plain("shear"));
    using TestWorld reached = Planted(["bar"], [Recipe(Stick, "shear")], Stick);
    ExlibChecks.Runs("walk", "walkshear", "walk:shear");
    ProcessJobRegistry.Shared.Contribute(
      new ProcessJobSet(
        ProcessJobSet.CurrentSchema,
        "walkshear",
        [new ProcessJob(Stick, "walk:bar", 1, null, null, 0f)]
      )
    );
    Assert.Empty(Walk(reached).Findings);

    using TestWorld unreached = Planted(["bar"], [], Stick);
    ExlibChecks.Runs("walk", "walkshear", "walk:shear");
    ProcessJobRegistry.Shared.Contribute(
      new ProcessJobSet(
        ProcessJobSet.CurrentSchema,
        "walkshear",
        [new ProcessJob(Stick, "walk:bar", 1, null, null, 0f)]
      )
    );
    Assert.Contains(
      Walk(unreached).Findings,
      f => f.StartsWith("walk:bar | ") && f.Contains("waits on walk:shear,")
    );
  }

  [Fact]
  public void A_route_stop_waits_on_the_next_thicker_stop_and_its_machine()
  {
    _files.Write("assets/walk/itemtypes/shear.json", Plain("shear"));
    _files.Write("assets/walk/itemtypes/thick.json", Plain("thick"));
    _files.Write("assets/walk/itemtypes/mid.json", Plain("mid"));
    var route = new ProcessRoute(
      ProcessRoute.CurrentSchema,
      "walkfamily",
      null,
      [
        new ProcessStage(3f, null, ["walkshear"], "walk:thick"),
        new ProcessStage(2f, null, ["walkshear"], "walk:mid"),
        new ProcessStage(1f, null, ["walkshear"], "walk:thin"),
      ]
    );
    using TestWorld reached = Planted(
      ["thin", "mid"],
      [Recipe(Stick, "shear"), Recipe(Stick, "thick")],
      Stick
    );
    ExlibChecks.Runs("walk", "walkshear", "walk:shear");
    ProcessRouteRegistry.Shared.Contribute(route);
    Assert.Empty(Walk(reached).Findings);

    using TestWorld nearest = Planted(
      ["thin"],
      [Recipe(Stick, "shear"), Recipe(Stick, "mid")],
      Stick
    );
    ExlibChecks.Runs("walk", "walkshear", "walk:shear");
    ProcessRouteRegistry.Shared.Contribute(route);
    Assert.Empty(Walk(nearest).Findings);

    using TestWorld noMid = Planted(["thin"], [Recipe(Stick, "shear")], Stick);
    ExlibChecks.Runs("walk", "walkshear", "walk:shear");
    ProcessRouteRegistry.Shared.Contribute(route);
    Assert.Contains(
      Walk(noMid).Findings,
      f => f.StartsWith("walk:mid | ") && f.Contains("waits on walk:thick,")
    );
  }

  [Fact]
  public void A_declaration_with_a_block_names_an_input_and_is_kept_once()
  {
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Produces("walk", "walk:made", "the press", "walk:press")
    );

    ExlibChecks.Produces("walk", "walk:made", "the press", "walk:press", Stick);
    ExlibChecks.Produces("walk", "walk:made", "the press", "walk:press", Stick);
    Assert.Single(ExlibChecks.Produced());
  }

  private const string Bench =
    "{ \"code\": \"bench\", \"class\": \"WalkFixtureBenchBlock\", \"entityClass\": \"WalkFixtureBench\" }";

  private TestWorld WithBench(
    string[] items,
    string[] recipes,
    params string[] vanilla
  )
  {
    _files.Write("assets/walk/blocktypes/bench.json", Bench);
    return Planted(items, recipes, [Stick, .. vanilla]);
  }

  // Fails when CraftAt stops throwing on a press that yields nothing.
  [Fact]
  public void A_station_that_yields_nothing_fails_as_refusing()
  {
    using TestWorld world = WithBench(
      ["a", "member"],
      [Recipe(Stick, "a"), Sized("member", 5, 5)]
    );
    WalkFixtureBench.Refused = "walk:member";

    CraftWalk.Result result = Walk(world);

    string finding = Assert.Single(result.Findings);
    Assert.StartsWith("walk:member | station refuses: ", finding);
    Assert.Contains("yielded nothing", finding);
    Assert.Contains("(1 executed, 1 followed)", result.Summary);
  }

  // Fails when Place skips CompleteConstruction, so a bench that refuses until built is walked unbuilt.
  [Fact]
  public void A_station_that_refuses_until_built_is_completed_before_it_is_walked()
  {
    _files.Write(
      "assets/walk/blocktypes/gated.json",
      """
      {
        "code": "gated",
        "entityClass": "WalkFixtureBench",
        "entityBehaviors": [
          {
            "name": "ExRightClickConstructable",
            "properties": { "stages": [{}, { "requireStacks": [] }] }
          }
        ]
      }
      """
    );
    using TestWorld world = Planted(
      ["a", "member"],
      [Recipe(Stick, "a"), Sized("member", 5, 5)],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    Assert.Contains("(2 executed, 0 followed)", result.Summary);
  }

  // Fails when Place completes a construction by RccFake without checking the entity keeps an _rcc or _animator field.
  [Fact]
  public void A_gated_station_that_caches_no_construction_field_is_walked()
  {
    _files.Write(
      "assets/walk/blocktypes/uncached.json",
      """
      {
        "code": "uncached",
        "entityClass": "WalkFixtureUncachedBench",
        "entityBehaviors": [
          {
            "name": "ExRightClickConstructable",
            "properties": { "stages": [{}, { "requireStacks": [] }] }
          }
        ]
      }
      """
    );
    using TestWorld world = Planted(
      ["a", "member"],
      [Recipe(Stick, "a"), Sized("member", 5, 5)],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    Assert.Contains("(2 executed, 0 followed)", result.Summary);
  }

  // Fails when the walk counts a recipe the matcher refused as executed.
  [Fact]
  public void A_hand_grid_recipe_the_matcher_refuses_fails_as_not_matching()
  {
    using TestWorld world = Planted(
      ["a", "b"],
      [Recipe(Stick, "a"), Recipe("walk:a", "b")],
      Stick
    );
    world.Api.Event.MatchesGridRecipe += (_, recipe, _, _) =>
      recipe.Name.ToString() != "walk:b";

    CraftWalk.Result result = Walk(world);

    string finding = Assert.Single(result.Findings);
    Assert.StartsWith("walk:b | does not match: ", finding);
    Assert.Contains("2 recipes fired (1 executed, 1 followed)", result.Summary);
  }

  private TestWorld Oiled()
  {
    _files.Write(
      "assets/walk/recipes/grid/oiled.json",
      CraftBillsLiquidTests.Recipe(0.5f)
    );
    return Planted(
      ["part", "made"],
      [Recipe(Stick, "part")],
      Stick,
      "game:woodbucket",
      "game:oilportion-olive"
    );
  }

  private const string BucketKit =
    "\"game:woodbucket\": { \"route\": \"seven planks\" }";

  private const string OilKit =
    "\"game:oilportion-olive\": { \"route\": \"pressed olives\" }";

  // Fails when the liquid edge is dropped from the recipe's needs, so the oil need goes unasked.
  [Fact]
  public void A_recipe_paying_oil_from_a_vessel_waits_on_the_oil()
  {
    using TestWorld world = Oiled();

    CraftWalk.Result result = Walk(world, kit: "{" + BucketKit + "}");

    string finding = Assert.Single(result.Findings);
    Assert.StartsWith(
      "game:oilportion-olive | asked for by walk:oiled",
      finding
    );
  }

  // Fails when the walk lays the vessel empty, so the matcher refuses the bill.
  [Fact]
  public void A_recipe_paying_oil_from_a_vessel_is_crafted_with_the_vessel_filled()
  {
    using TestWorld world = Oiled();

    CraftWalk.Result result = Walk(
      world,
      kit: "{" + BucketKit + "," + OilKit + "}"
    );

    Assert.Empty(result.Findings);
    Assert.Contains(
      new CraftWalk.CraftedEdge(
        "walk:made",
        "walk:oiled",
        CraftWalk.HandGridName
      ),
      result.Crafted
    );
  }

  // Fails when SetupScene.Craft leaves the vessel a liquid bill keeps in its grid slot, so the second recipe
  // meets the first one's vessel in a slot its pattern leaves empty and the station refuses it.
  [Fact]
  public void A_vessel_a_liquid_bill_keeps_is_taken_out_of_the_stations_grid()
  {
    _files.Write(
      "assets/walk/blocktypes/tray.json",
      """{ "code": "tray", "entityClass": "WalkFixtureTrayBench" }"""
    );
    string oiled = CraftBillsLiquidTests
      .Recipe(0.5f)
      .Replace("\"width\": 2", "\"width\": 4");
    _files.Write(
      "assets/walk/recipes/grid/oiled.json",
      "["
        + oiled.Replace("\"PF\"", "\"PF__\"")
        + ","
        + oiled
          .Replace("\"PF\"", "\"P_F_\"")
          .Replace("walk:oiled", "walk:oiled-apart")
          .Replace("walk:made", "walk:made-apart")
        + "]"
    );
    _files.Write("assets/walk/itemtypes/made.json", Plain("made"));
    _files.Write("assets/walk/itemtypes/made-apart.json", Plain("made-apart"));
    using TestWorld world = Planted(
      ["part"],
      [Recipe(Stick, "part")],
      Stick,
      "game:woodbucket",
      "game:oilportion-olive"
    );

    CraftWalk.Result result = Walk(
      world,
      kit: "{" + BucketKit + "," + OilKit + "}"
    );

    Assert.Empty(result.Findings);
    Assert.Equal(
      ["walk:made", "walk:made-apart"],
      result
        .Crafted.Where(c => c.Station != CraftWalk.HandGridName)
        .Select(c => c.Output)
        .Order()
    );
  }

  // Fails when the walk lays the first loaded variant of a wildcard, not the first reached one.
  [Fact]
  public void A_wildcard_lays_the_first_variant_the_walk_reached()
  {
    string[] variants = ["w-1", "w-2"];
    foreach (string variant in variants)
      _files.Write($"assets/walk/itemtypes/{variant}.json", Plain(variant));
    string[] loaded;
    using (TestWorld probe = WithBench(["member"], []))
      loaded =
      [
        .. probe
          .World.Items.Where(i =>
            i?.Code.Domain == "walk" && variants.Contains(i.Code.Path)
          )
          .Select(i => i.Code.Path),
      ];
    using TestWorld world = WithBench(
      ["member"],
      [Recipe(Stick, loaded[1]), Sized("member", 5, 5, "walk:w-*")]
    );

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    Assert.Equal(["walk:" + loaded[1]], WalkFixtureBench.Laid);
  }

  // Fails when the width check in HandGrid.Orders goes: the hand grid then takes the set member.
  [Fact]
  public void A_set_member_is_crafted_at_the_bench_and_the_hand_grid_refuses_it()
  {
    using TestWorld world = WithBench(
      ["a", "member"],
      [Recipe(Stick, "a"), Sized("member", 5, 5)]
    );

    CraftWalk.Result result = Walk(world);

    Assert.Empty(result.Findings);
    Assert.Equal(["walk:a"], WalkFixtureBench.Laid);
    Assert.Contains("2 recipes fired (2 executed, 0 followed)", result.Summary);
    Assert.Equal(
      [
        new CraftWalk.CraftedEdge("walk:a", "walk:a", CraftWalk.HandGridName),
        new CraftWalk.CraftedEdge("walk:member", "walk:member", "walk:bench"),
      ],
      result.Crafted
    );
  }

  // Fails when a fired station recipe that no placed station holds is skipped without a finding.
  [Fact]
  public void A_set_member_no_placed_station_holds_fails()
  {
    using TestWorld world = Planted(
      ["a", "member"],
      [Recipe(Stick, "a"), Sized("member", 5, 5)],
      Stick
    );

    CraftWalk.Result result = Walk(world);

    Assert.Equal(
      ["walk:member | no placed station holds walk:member"],
      result.Findings
    );
    Assert.Contains("(1 executed, 1 followed)", result.Summary);
    Assert.DoesNotContain(result.Crafted, c => c.Output == "walk:member");
  }
}
#endif
