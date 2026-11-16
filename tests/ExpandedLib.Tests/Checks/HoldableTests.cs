#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

// Returns a stack of walk:probed, which its resolved drops do not name.
internal sealed class HoldableProbeBlock : Block
{
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [new ItemStack(world.GetItem(new AssetLocation("walk:probed")), 1)];
}

internal sealed class HoldableThrowBlock : Block
{
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) => throw new InvalidOperationException("no drops");
}

/// <summary><see cref="Holdable"/> over planted mods.</summary>
public sealed class HoldableTests(ITestOutputHelper output) : IDisposable
{
  private readonly PlantedFiles _files = new();

  public void Dispose()
  {
    _files.Dispose();
    ExlibChecks.ClearDeclarations();
  }

  private const string Stick = "game:stick";

  private static string Item(string code, string creative = "") =>
    $"{{ \"code\": \"{code}\"{creative} }}";

  private const string Creative =
    ", \"creativeinventory\": { \"general\": [\"*\"] }";

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

  private void Plant(string kind, string code, string json) =>
    _files.Write($"assets/walk/{kind}/{code}.json", json);

  private TestWorld Planted(params string[] recipes)
  {
    _files.Write(
      "modinfo.json",
      "{ \"type\": \"content\", \"modid\": \"walk\", \"name\": \"Walk\", \"version\": \"1.0.0\" }"
    );
    _files.Write(
      "assets/walk/recipes/grid/walk.json",
      "[" + string.Join(",", recipes) + "]"
    );
    var world = new TestWorld();
    world
      .RegisterClass("HoldableProbeBlock", typeof(HoldableProbeBlock))
      .RegisterClass("HoldableThrowBlock", typeof(HoldableThrowBlock));
    world.LoadAssets([_files.Root], null, [Stick]);
    world.LoadRecipes();
    return world;
  }

  private Holdable.Census Census(TestWorld world, string? kit = null)
  {
    Holdable.Census census = Holdable.Run(
      world,
      "walk",
      kit == null ? null : _files.Write("kit.json", kit)
    );
    foreach (Holdable.Entry e in census.Entries)
      output.WriteLine($"{e.Code}\t{e.State}\t{e.Why}\t{e.Creative}");
    return census;
  }

  private static Holdable.Entry Of(Holdable.Census census, string code) =>
    census.Entries.Single(e => e.Code == code);

  private const string Rock =
    "{ \"code\": \"rock\", \"drops\": [ { \"type\": \"item\", \"code\": \"walk:x\" } ] }";

  // Fails when the census leaves out the output of a recipe.
  [Fact]
  [PlantedDefect(typeof(Holdable), nameof(Holdable.Run))]
  public void A_recipe_output_is_held_and_names_the_recipe()
  {
    Plant("itemtypes", "a", Item("a"));
    using TestWorld world = Planted(Recipe(Stick, "a"));

    Holdable.Entry entry = Of(Census(world), "walk:a");

    Assert.Equal(Holdable.State.Yes, entry.State);
    Assert.Equal("walk:a", entry.Why);
  }

  // Fails when a code nothing yields is not No, or its why is not none.
  [Fact]
  public void A_code_nothing_yields_is_not_held()
  {
    Plant("itemtypes", "lonely", Item("lonely"));
    using TestWorld world = Planted();

    Holdable.Census census = Census(world);

    Holdable.Entry entry = Of(census, "walk:lonely");
    Assert.Equal(Holdable.State.No, entry.State);
    Assert.Equal("none", entry.Why);
    Assert.Equal("not holdable", census.SkipOf("walk:lonely"));
  }

  // Fails when the drops of a block are not read as a source.
  [Fact]
  public void A_new_drop_makes_its_code_held()
  {
    Plant("itemtypes", "x", Item("x"));
    using TestWorld without = Planted();
    Assert.Equal(Holdable.State.No, Of(Census(without), "walk:x").State);

    Plant("blocktypes", "rock", Rock);
    using TestWorld with = Planted();

    Holdable.Entry entry = Of(Census(with), "walk:x");
    Assert.Equal(Holdable.State.Yes, entry.State);
    Assert.Equal("drop of walk:rock", entry.Why);
  }

  // Fails when a block whose class overrides GetDrops is judged by its resolved drops.
  [Fact]
  public void A_block_that_overrides_its_drops_is_asked_for_them()
  {
    Plant("itemtypes", "probed", Item("probed"));
    Plant(
      "blocktypes",
      "probe",
      "{ \"code\": \"probe\", \"class\": \"HoldableProbeBlock\" }"
    );
    using TestWorld world = Planted();

    Holdable.Entry entry = Of(Census(world), "walk:probed");

    Assert.Equal(Holdable.State.Yes, entry.State);
    Assert.Equal("drop of walk:probe", entry.Why);
  }

  // Fails when a GetDrops that throws leaves its block No instead of unsure.
  [Fact]
  public void A_block_whose_drops_throw_is_unsure()
  {
    Plant(
      "blocktypes",
      "thrower",
      "{ \"code\": \"thrower\", \"class\": \"HoldableThrowBlock\" }"
    );
    using TestWorld world = Planted();

    Holdable.Census census = Census(world);

    Holdable.Entry entry = Of(census, "walk:thrower");
    Assert.Equal(Holdable.State.Unsure, entry.State);
    Assert.Equal("drops not probed: InvalidOperationException", entry.Why);
    Assert.Null(census.SkipOf("walk:thrower"));
  }

  // Fails when a block's drop of itself makes it held.
  [Fact]
  public void A_default_self_drop_holds_nothing()
  {
    Plant("blocktypes", "lone", "{ \"code\": \"lone\" }");
    using TestWorld world = Planted();

    Holdable.Entry entry = Of(Census(world), "walk:lone");

    Assert.Equal(Holdable.State.No, entry.State);
    Assert.Equal("none", entry.Why);
  }

  private const string Post =
    "{ \"code\": \"post\", \"variantgroups\": [ { \"code\": \"rotation\", \"states\": [ \"ud\", \"ns\" ] } ], \"behaviors\": [ { \"name\": \"Pillar\" } ] }";

  // Fails when a block's drop of itself never counts, so an orientation of a crafted block is
  // not held.
  [Fact]
  public void An_orientation_of_a_crafted_block_is_held_by_its_self_drop()
  {
    Plant("blocktypes", "post", Post);
    using TestWorld world = Planted(Recipe(Stick, "post-ud", "block"));

    Holdable.Census census = Census(world);

    Assert.Equal(Holdable.State.Yes, Of(census, "walk:post-ud").State);
    Holdable.Entry other = Of(census, "walk:post-ns");
    Assert.Equal(Holdable.State.Yes, other.State);
    Assert.Equal("drop of walk:post-ns", other.Why);
  }

  private const string HoopDrops =
    "{ \"code\": \"hooped\", \"drops\": [ { \"type\": \"item\", \"code\": \"walk:brick\" }, { \"type\": \"item\", \"code\": \"walk:hoop\" } ] }";

  // Fails when a produced block whose own drop yields it is not held.
  [Fact]
  public void A_produced_block_whose_drop_yields_itself_is_held()
  {
    Plant("blocktypes", "cast", "{ \"code\": \"cast\" }");
    using TestWorld world = Planted();
    ExlibChecks.Produces("walk", "walk:cast", "the pour");

    Holdable.Entry entry = Of(Census(world), "walk:cast");

    Assert.Equal(Holdable.State.Yes, entry.State);
    Assert.Equal("produced by the pour", entry.Why);
  }

  // Fails when a produced block whose drops yield other codes is held, or its why leaves the
  // codes out.
  [Fact]
  public void A_produced_block_whose_drop_yields_other_codes_is_not_held()
  {
    Plant("blocktypes", "hooped", HoopDrops);
    Plant("itemtypes", "brick", Item("brick"));
    Plant("itemtypes", "hoop", Item("hoop"));
    using TestWorld world = Planted();
    ExlibChecks.Produces("walk", "walk:hooped", "the hooping");

    Holdable.Census census = Census(world);

    Holdable.Entry entry = Of(census, "walk:hooped");
    Assert.Equal(Holdable.State.No, entry.State);
    Assert.Contains("walk:brick, walk:hoop", entry.Why);
    Assert.Equal("not holdable", census.SkipOf("walk:hooped"));
    Assert.Equal(Holdable.State.Yes, Of(census, "walk:brick").State);
  }

  // Fails when a machine product block whose drops yield other codes is held.
  [Fact]
  public void A_machine_product_block_whose_drop_yields_other_codes_is_not_held()
  {
    Plant("blocktypes", "hooped", HoopDrops);
    Plant("itemtypes", "brick", Item("brick"));
    Plant("itemtypes", "hoop", Item("hoop"));
    using TestWorld world = Planted();
    ExlibChecks.Produces(
      "walk",
      "walk:hooped",
      "the hooping",
      "walk:brick",
      Stick
    );

    Holdable.Entry entry = Of(Census(world), "walk:hooped");

    Assert.Equal(Holdable.State.No, entry.State);
    Assert.Contains("the hooping", entry.Why);
  }

  // Fails when a produced block whose drops cannot be read is No instead of unsure.
  [Fact]
  public void A_produced_block_whose_drops_throw_is_unsure()
  {
    Plant(
      "blocktypes",
      "thrower",
      "{ \"code\": \"thrower\", \"class\": \"HoldableThrowBlock\" }"
    );
    using TestWorld world = Planted();
    ExlibChecks.Produces("walk", "walk:thrower", "the pour");

    Holdable.Census census = Census(world);

    Assert.Equal(Holdable.State.Unsure, Of(census, "walk:thrower").State);
    Assert.Null(census.SkipOf("walk:thrower"));
  }

  // Fails when a produced item is judged by drops.
  [Fact]
  public void A_produced_item_is_held()
  {
    Plant("itemtypes", "made", Item("made"));
    using TestWorld world = Planted();
    ExlibChecks.Produces("walk", "walk:made", "the press");

    Assert.Equal(Holdable.State.Yes, Of(Census(world), "walk:made").State);
  }

  // Fails when SkipOf ignores the creative tab.
  [Fact]
  public void A_creative_listed_code_nothing_yields_is_skipped_as_creative_only()
  {
    Plant("itemtypes", "ghost", Item("ghost", Creative));
    using TestWorld world = Planted();

    Holdable.Census census = Census(world);

    Holdable.Entry entry = Of(census, "walk:ghost");
    Assert.Equal(Holdable.State.No, entry.State);
    Assert.Equal("creative only", entry.Why);
    Assert.True(entry.Creative);
    Assert.Equal("creative only", census.SkipOf("walk:ghost"));
  }

  // Fails when a code a recipe needs and nothing yields is called No.
  [Fact]
  public void A_code_a_recipe_needs_with_no_source_is_unsure()
  {
    Plant("itemtypes", "needed", Item("needed"));
    Plant("itemtypes", "out", Item("out"));
    using TestWorld world = Planted(Recipe("walk:needed", "out"));

    Holdable.Census census = Census(world);

    Holdable.Entry needed = Of(census, "walk:needed");
    Assert.Equal(Holdable.State.Unsure, needed.State);
    Assert.StartsWith("asked for by walk:out", needed.Why);
    Assert.Null(census.SkipOf("walk:needed"));
    Assert.Equal(Holdable.State.Yes, Of(census, "walk:out").State);
  }

  // Fails when the product of a transition is not read as a source.
  [Fact]
  public void The_product_of_a_transition_is_held()
  {
    Plant(
      "itemtypes",
      "wet",
      """
      {
        "code": "wet",
        "transitionableProps": [
          {
            "type": "Dry",
            "freshHours": { "avg": 1 },
            "transitionHours": { "avg": 1 },
            "transitionedStack": { "type": "item", "code": "walk:dried" }
          }
        ]
      }
      """
    );
    Plant("itemtypes", "dried", Item("dried"));
    using TestWorld world = Planted(Recipe(Stick, "wet"));

    Holdable.Entry entry = Of(Census(world), "walk:dried");

    Assert.Equal(Holdable.State.Yes, entry.State);
    Assert.Equal("transition of walk:wet", entry.Why);
  }

  private static string? With(string variable, string? value, Func<string?> run)
  {
    string? before = Environment.GetEnvironmentVariable(variable);
    Environment.SetEnvironmentVariable(variable, value);
    try
    {
      return run();
    }
    finally
    {
      Environment.SetEnvironmentVariable(variable, before);
    }
  }

  // Fails when Write writes for an unset or empty variable, or leaves out a row or a count line.
  [Fact]
  public void Write_names_the_file_by_its_variable_and_counts_the_codes()
  {
    Plant("itemtypes", "a", Item("a"));
    Plant("itemtypes", "ghost", Item("ghost", Creative));
    using TestWorld world = Planted(Recipe(Stick, "a"));
    Holdable.Census census = Census(world);
    const string variable = "EX_HOLDABLE_TESTS";
    string path = _files.Path("out/holdable.tsv");

    Assert.Null(With(variable, null, () => Holdable.Write(census, variable)));
    Assert.Null(With(variable, "", () => Holdable.Write(census, variable)));
    Assert.False(File.Exists(path));
    Assert.Equal(
      path,
      With(variable, path, () => Holdable.Write(census, variable))
    );

    Assert.Equal(
      [
        "code\ttype\tholdable\twhy\tcreative",
        "walk:a\titem\tyes\twalk:a\tno",
        "walk:ghost\titem\tno\tcreative only\tyes",
        "#total\t2",
        "#yes\t1",
        "#no\t1",
        "#creative only\t1",
        "#unsure\t0",
        "#unsure drops not probed\t0",
        "#unsure asked for\t0",
        "#unsure own drop only\t0",
      ],
      File.ReadAllLines(path)
    );
  }
}
#endif
