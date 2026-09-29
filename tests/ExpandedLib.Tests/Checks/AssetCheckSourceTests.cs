using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExpandedLib.Catalogues;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="AssetCheckSource.Domains"/> against a <see cref="TestModLoader"/>: only exlib and its
/// declared dependents are in scope, never a mod with no exlib dependency at all.
/// </summary>
public class AssetCheckSourceTests {
  [Fact]
  public void Domains_is_exlib_plus_its_dependents_only() {
    using var world = new TestWorld();
    world.Mods.Add("dependent", "1.0.0", dependencies: "exlib");
    world.Mods.Add("bystander", "1.0.0");

    var source = new AssetCheckSource(world.Api);

    Assert.Equal(["dependent", "exlib"], source.Domains.OrderBy(d => d));
  }

  // Fails when AssetCheckSource.Blocks or Items keeps a collectible the game marks IsMissing.
  [Fact]
  public void The_stand_ins_for_codes_nothing_registers_are_left_out() {
    using var world = new TestWorld();
    world
      .Register(new Block { Code = new("dependent:crate"), BlockId = 900 })
      .Register(
        new Block {
          Code = new("dependent:gone"),
          BlockId = 901,
          IsMissing = true,
        }
      );
    world.RegisterItem("dependent:plank");
    world.Register(
      new Item {
        Code = new("dependent:goneitem"),
        ItemId = 902,
        IsMissing = true,
      }
    );
    var source = new AssetCheckSource(world.Api);

    Assert.Contains(new AssetLocation("dependent:crate"), source.BlockCodes);
    Assert.DoesNotContain(
      new AssetLocation("dependent:gone"),
      source.BlockCodes
    );
    Assert.Contains(new AssetLocation("dependent:plank"), source.ItemCodes);
    Assert.DoesNotContain(
      new AssetLocation("dependent:goneitem"),
      source.ItemCodes
    );
    Assert.DoesNotContain(
      source.Collectibles,
      c => c.Code.Path is "gone" or "goneitem"
    );
  }

  // Fails when BlockTypes stops reading the domain's blocktype assets or keeps an unparsable one.
  [Fact]
  public void BlockTypes_reads_each_parsable_blocktype_of_the_domain() {
    using var world = new TestWorld();
    IAsset Asset(string path, string text) =>
      ExSyntheticAsset.Create(
        new AssetLocation("dependent", path),
        Encoding.UTF8.GetBytes(text),
        new ExDefinitionOrigin()
      );
    world
      .Api.Assets.GetMany("blocktypes/", "dependent")
      .Returns([
        Asset("blocktypes/crate.json", "{ \"code\": \"crate\" }"),
        Asset("blocktypes/broken.json", "{ \"code\": "),
      ]);

    var (file, json) = Assert.Single(
      new AssetCheckSource(world.Api).BlockTypes("dependent")
    );

    Assert.Equal("dependent:blocktypes/crate.json", file.ToString());
    Assert.Equal("crate", (string?)json["code"]);
  }

  // Fails when RecipeOutputs stops reading a terminal job's output, a route's stopping point or a
  // loaded die's job output, or reads a route stage that names no code.
  [Fact]
  public void RecipeOutputs_names_what_exlibs_process_catalogues_make() {
    using var world = new TestWorld();
    Item die = world.RegisterItem("dependent:die-planted");
    die.Attributes = new JsonObject(
      JToken.FromObject(
        ItemDie.Job("plantedbench", "dependent:blank", "dependent:dieout")
      )
    );
    try {
      ProcessJobRegistry.Shared.Contribute(
        new ProcessJobSet(
          ProcessJobSet.CurrentSchema,
          "plantedmachine",
          [
            new ProcessJob(
              "dependent:in",
              "dependent:jobout",
              1,
              null,
              null,
              0f
            ),
          ]
        )
      );
      ProcessRouteRegistry.Shared.Contribute(
        new ProcessRoute(
          ProcessRoute.CurrentSchema,
          "plantedfamily",
          null,
          [
            new ProcessStage(1f, null, ["plantedfamily"], "dependent:stageout"),
            new ProcessStage(0.5f, null, ["plantedfamily"], null),
          ]
        )
      );

      Assert.Equal(
        [
          "processjobs item dependent:jobout",
          "processroutes item dependent:stageout",
          "die item dependent:dieout",
        ],
        new AssetCheckSource(world.Api).RecipeOutputs.Select(o =>
          $"{o.Registry} {o.Type.ToString().ToLowerInvariant()} {o.Code}"
        )
      );
    } finally {
      ProcessJobRegistry.ResetForWorld();
      ProcessRouteRegistry.ResetForWorld();
    }
  }

  // Fails when RecipeOutputs stops reading any one of the six recipe registries' outputs.
  [Fact]
  public void RecipeOutputs_names_what_the_recipe_registries_make() {
    using var world = new TestWorld();
    JsonItemStack Out(string code) =>
      new() { Type = EnumItemClass.Item, Code = new AssetLocation(code) };
    var registry = new RecipeRegistrySystem();
    registry.CookingRecipes.Add(
      new CookingRecipe { CooksInto = Out("dependent:cooked") }
    );
    registry.BarrelRecipes.Add(
      new BarrelRecipe {
        Output = new BarrelOutputStack {
          Type = EnumItemClass.Item,
          Code = new AssetLocation("dependent:soaked"),
        },
      }
    );
    registry.MetalAlloys.Add(
      new AlloyRecipe { Output = Out("dependent:alloyed") }
    );
    registry.SmithingRecipes.Add(
      new SmithingRecipe { Output = Out("dependent:smithed") }
    );
    registry.KnappingRecipes.Add(
      new KnappingRecipe { Output = Out("dependent:knapped") }
    );
    registry.ClayFormingRecipes.Add(
      new ClayFormingRecipe { Output = Out("dependent:formed") }
    );
    world.Mods.Register(registry);

    Assert.Equal(
      [
        "cooking dependent:cooked",
        "barrel dependent:soaked",
        "alloy dependent:alloyed",
        "smithing dependent:smithed",
        "knapping dependent:knapped",
        "clayforming dependent:formed",
      ],
      new AssetCheckSource(world.Api).RecipeOutputs.Select(o =>
        $"{o.Registry} {o.Code}"
      )
    );
  }

  // Fails when an asset whose data the server unloaded stops loading again from its origin.
  [Fact]
  public void A_code_first_asset_the_server_unloads_loads_again_from_its_origin() {
    byte[] data = Encoding.UTF8.GetBytes("{ \"code\": \"crate\" }");
    IAsset asset = ExSyntheticAsset.Create(
      new AssetLocation("dependent", "blocktypes/crate.json"),
      data,
      new ExDefinitionOrigin()
    );
    asset.Data = null;

    Assert.True(asset.Origin.TryLoadAsset(asset));
    Assert.Equal(data, asset.Data);
  }

  #region Held recipes

  private const string Held = "heldplanted";

  // Five grid recipes of one input, a gear: #0 named, #1 to #4 unnamed and so all named by the
  // file; and #2, named, of another input, a rod.
  private const string Machines = """
    [
      { "name": "Boiler", "ingredientPattern": "G", "width": 1, "height": 1,
        "ingredients": { "G": { "type": "item", "code": "heldplanted:gear" } },
        "output": { "type": "block", "code": "heldplanted:boiler" } },
      { "ingredientPattern": "G", "width": 1, "height": 1,
        "ingredients": { "G": { "type": "item", "code": "heldplanted:gear" } },
        "output": { "type": "block", "code": "heldplanted:engine-{kind}" } },
      { "name": "Pump", "ingredientPattern": "R", "width": 1, "height": 1,
        "ingredients": { "R": { "type": "item", "code": "heldplanted:rod" } },
        "output": { "type": "block", "code": "heldplanted:pump" } },
      { "ingredientPattern": "G", "width": 1, "height": 1,
        "ingredients": { "G": { "type": "item", "code": "heldplanted:gear" } },
        "output": { "type": "block", "code": "heldplanted:crate" } },
      { "ingredientPattern": "G", "width": 1, "height": 1,
        "ingredients": { "G": { "type": "item", "code": "heldplanted:gear" } },
        "output": { "type": "block", "code": "heldplanted:chest" } }
    ]
    """;

  private static readonly AssetLocation MachinesFile = new(
    Held,
    "recipes/grid/machines.json"
  );

  private static IAsset RecipeAsset(AssetLocation file, string text) =>
    ExSyntheticAsset.Create(
      file,
      Encoding.UTF8.GetBytes(text),
      new ExDefinitionOrigin()
    );

  private static GridRecipe Holding(AssetLocation name, string output) =>
    new() {
      Name = name,
      Output = new CraftingRecipeIngredient {
        Type = EnumItemClass.Block,
        Code = new AssetLocation(output),
      },
    };

  // A world whose grid registry held the given recipes of MachinesFile when the server finished
  // loading assets, and holds them still.
  private static TestWorld MachinesWorld(params GridRecipe[] held) {
    var world = new TestWorld();
    world.Mods.Add(Held, "1.0.0", dependencies: "exlib");
    world
      .Api.Assets.GetMany("recipes/", Held)
      .Returns([RecipeAsset(MachinesFile, Machines)]);
    world.World.GridRecipes.Returns([.. held]);
    AssetCheckSource.NoteHeldRecipes(world.Api);
    return world;
  }

  private static readonly GridRecipe[] AllMachines =
  [
    Holding(AssetLocation.Create("Boiler", Held), "heldplanted:boiler"),
    Holding(MachinesFile, "heldplanted:engine-steam"),
    Holding(AssetLocation.Create("Pump", Held), "heldplanted:pump"),
    Holding(MachinesFile, "heldplanted:crate"),
    Holding(MachinesFile, "heldplanted:chest"),
  ];

  private static string Collision(int a, string labelA, int b, string labelB) =>
    $"{Held}:recipes/grid/machines.json#{a} ({labelA}) and "
    + $"{Held}:recipes/grid/machines.json#{b} ({labelB}) match the same input";

  // Fails when Recipes drops a recipe its registry held at load and holds still, under the JSON
  // name or the file, or reads a file's {kind} as literal text.
  [Fact]
  public void A_recipe_the_game_still_holds_collides_and_asks() {
    using TestWorld world = MachinesWorld(AllMachines);
    var source = new AssetCheckSource(world.Api);

    Assert.Contains(
      Collision(0, "Boiler", 1, "heldplanted:engine-{kind}"),
      GridRecipeCollisionCheck.Run(source, Held).Errors
    );
    IReadOnlyList<string> asks = ObtainabilityCheck.Run(source, Held).Errors;
    Assert.Contains(asks, e => e.Contains("heldplanted:gear (item"));
    Assert.Contains(asks, e => e.Contains("heldplanted:rod (item"));
  }

  // Fails when Recipes reads a recipe its registry held at load and no longer holds, holds a
  // recipe by its name alone without its output, or renumbers the recipes after one it leaves out.
  [Fact]
  public void A_recipe_removed_after_load_neither_collides_nor_asks() {
    using TestWorld world = MachinesWorld(AllMachines);
    world.World.GridRecipes.Returns([AllMachines[1], AllMachines[3]]);
    var source = new AssetCheckSource(world.Api);

    Assert.Equal(
      [Collision(1, "heldplanted:engine-{kind}", 3, "heldplanted:crate")],
      GridRecipeCollisionCheck.Run(source, Held).Errors
    );
    IReadOnlyList<string> asks = ObtainabilityCheck.Run(source, Held).Errors;
    Assert.Contains(asks, e => e.Contains("heldplanted:gear (item"));
    Assert.DoesNotContain(asks, e => e.Contains("heldplanted:rod"));
  }

  // Fails when Recipes leaves out a recipe its registry never held, as the game's refusal at load
  // leaves it, or reads the notes of another game.
  [Fact]
  public void A_recipe_the_game_refused_at_load_is_read_and_collides() {
    using TestWorld world = MachinesWorld(
      AllMachines[0],
      AllMachines[2],
      AllMachines[3],
      AllMachines[4]
    );
    using TestWorld other = MachinesWorld(AllMachines);
    other.World.GridRecipes.Returns([.. AllMachines.Where((_, i) => i != 1)]);
    var source = new AssetCheckSource(world.Api);

    Assert.Equal(5, source.Recipes(Held).Count());
    Assert.Contains(
      Collision(0, "Boiler", 1, "heldplanted:engine-{kind}"),
      GridRecipeCollisionCheck.Run(source, Held).Errors
    );
  }

  // Fails when Recipes matches a recipe's Name without its domain.
  [Fact]
  public void Two_recipes_named_alike_in_two_domains_are_told_apart() {
    using var world = new TestWorld();
    const string Boiler = """
      { "name": "Cornish Boiler", "ingredientPattern": "P", "width": 1, "height": 1,
        "ingredients": { "P": { "type": "item", "code": "game:plank" } },
        "output": { "type": "block", "code": "game:crate" } }
      """;
    foreach (string domain in new[] { Held, "heldother" }) {
      world.Mods.Add(domain, "1.0.0", dependencies: "exlib");
      world
        .Api.Assets.GetMany("recipes/", domain)
        .Returns([
          RecipeAsset(new(domain, "recipes/grid/boiler.json"), Boiler),
        ]);
    }
    GridRecipe other = Holding(
      AssetLocation.Create("Cornish Boiler", "heldother"),
      "game:crate"
    );
    world.World.GridRecipes.Returns([
      Holding(AssetLocation.Create("Cornish Boiler", Held), "game:crate"),
      other,
    ]);
    AssetCheckSource.NoteHeldRecipes(world.Api);
    world.World.GridRecipes.Returns([other]);
    var source = new AssetCheckSource(world.Api);

    Assert.Empty(source.Recipes(Held));
    Assert.Equal(
      "heldother:recipes/grid/boiler.json",
      Assert.Single(source.Recipes("heldother")).File.ToString()
    );
  }

  // Fails when Recipes drops another folder's recipe, one of a registry not run at load or one with
  // no output, or reads one its running registry held at load and no longer holds.
  [Fact]
  public void Only_the_registries_the_game_runs_filter_their_folders() {
    using var world = new TestWorld();
    world.Mods.Add(Held, "1.0.0", dependencies: "exlib");
    const string Plate = """
      { "ingredient": { "type": "item", "code": "heldplanted:bar" },
        "output": { "type": "item", "code": "heldplanted:plate" } }
      """;
    AssetLocation noOutput = new(Held, "recipes/grid/nooutput.json");
    world
      .Api.Assets.GetMany("recipes/", Held)
      .Returns([
        RecipeAsset(new(Held, "recipes/smithing/plate.json"), Plate),
        RecipeAsset(new(Held, "recipes/alloy/bronze.json"), Plate),
        RecipeAsset(
          noOutput,
          """{ "ingredientPattern": "G", "width": 1, "height": 1 }"""
        ),
      ]);
    world.World.GridRecipes.Returns([Holding(noOutput, "heldplanted:any")]);
    AssetCheckSource.NoteHeldRecipes(world.Api);
    world.World.GridRecipes.Returns([]);
    var registry = new RecipeRegistrySystem();
    world.Mods.Register(registry);

    Assert.Equal(
      [
        "recipes/smithing/plate.json",
        "recipes/alloy/bronze.json",
        "recipes/grid/nooutput.json",
      ],
      new AssetCheckSource(world.Api).Recipes(Held).Select(r => r.File.Path)
    );

    registry.SmithingRecipes.Add(
      new SmithingRecipe {
        Name = new(Held, "recipes/smithing/plate.json"),
        Output = new JsonItemStack {
          Type = EnumItemClass.Item,
          Code = new("heldplanted:plate"),
        },
      }
    );
    AssetCheckSource.NoteHeldRecipes(world.Api);
    registry.SmithingRecipes.Clear();

    Assert.Equal(
      ["recipes/alloy/bronze.json", "recipes/grid/nooutput.json"],
      new AssetCheckSource(world.Api).Recipes(Held).Select(r => r.File.Path)
    );
  }

  #endregion

#if GAME_GE_1_22
  // Fails when the harness stops loading a collectible's tags, or Tagged stops matching an
  // ingredient's tags against them or takes an item of the other class.
  [Fact]
  public void Tagged_takes_the_loaded_items_carrying_the_ingredients_tag()
  {
    string mod = Directory
      .CreateTempSubdirectory("exlib-tagged-test-")
      .FullName;
    try
    {
      File.WriteAllText(
        Path.Combine(mod, "modinfo.json"),
        """{ "type": "content", "modid": "plantedtags", "version": "1.0.0" }"""
      );
      string items = Path.Combine(mod, "assets", "plantedtags", "itemtypes");
      Directory.CreateDirectory(items);
      File.WriteAllText(
        Path.Combine(items, "tagged.json"),
        """{ "code": "tagged", "tags": ["plantedtag"] }"""
      );
      File.WriteAllText(
        Path.Combine(items, "untagged.json"),
        """{ "code": "untagged" }"""
      );
      using var world = new TestWorld();
      world.LoadAssets(mod);
      var source = new AssetCheckSource(world.Api);

      Assert.Equal(
        ["plantedtags:tagged"],
        source
          .Tagged(
            JObject.Parse("""{ "type": "item", "tags": ["plantedtag"] }""")
          )!
          .Select(c => c.ToString())
      );
      Assert.Empty(
        source.Tagged(
          JObject.Parse("""{ "type": "block", "tags": ["plantedtag"] }""")
        )!
      );
    }
    finally
    {
      Directory.Delete(mod, recursive: true);
    }
  }
#endif
}
