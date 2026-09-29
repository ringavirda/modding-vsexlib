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
