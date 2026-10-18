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
    [MachineSlotSpec.AnyInput(), MachineSlotSpec.Output()];

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
        .GridRecipes.Where(r => r.Width > HandGrid.Size)
        .Select(r => new CraftOrder(
          r.Name.ToString(),
          [
            .. r.ResolvedIngredients!.OfType<CraftingRecipeIngredient>()
              .Select(
                (ingredient, i) => new CraftInput(0, ingredient.Id, ingredient)
              ),
          ],
          Press,
          Encoding.UTF8.GetBytes(r.Name.ToString()),
          [1],
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
    string? known = null
  )
  {
    CraftWalk.Result result = CraftWalk.Run(
      world,
      "walk",
      kit == null ? null : _files.Write("kit.json", kit),
      known == null ? null : _files.Write("known.json", known)
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
    "{ \"code\": \"bench\", \"entityClass\": \"WalkFixtureBench\" }";

  private TestWorld WithBench(string[] items, string[] recipes)
  {
    _files.Write("assets/walk/blocktypes/bench.json", Bench);
    return Planted(items, recipes, Stick);
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
