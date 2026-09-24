using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="ObtainabilityCheck"/> over a planted domain whose recipes ask for codes the
/// stub game does and does not make, and <see cref="ExlibChecks.Produces"/>'s
/// declarations.</summary>
public sealed class ObtainabilityCheckTests : IDisposable {
  private readonly ITestOutputHelper output;

  public ObtainabilityCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
    ExlibChecks.ClearDeclarations();
  }

  public void Dispose() => ExlibChecks.ClearDeclarations();

  private const string File = "stub:recipes/grid/planted.json";

  /// <summary>A stub source with one grid recipe asking for each of
  /// <paramref name="ingredients"/>, given as <c>(type, code)</c>, one pattern letter each.</summary>
  private static RecipeStubSource Asking(
    params (string Type, string Code)[] ingredients
  ) {
    string letters = new([
      .. Enumerable.Range(0, ingredients.Length).Select(i => (char)('A' + i)),
    ]);
    var recipe = new JObject {
      ["ingredientPattern"] = letters,
      ["width"] = ingredients.Length,
      ["height"] = 1,
      ["ingredients"] = new JObject(
        ingredients.Select(
          (ing, i) =>
            new JProperty(
              letters[i].ToString(),
              new JObject { ["type"] = ing.Type, ["code"] = ing.Code }
            )
        )
      ),
      ["output"] = new JObject { ["type"] = "item", ["code"] = "stub:product" },
    };
    return new RecipeStubSource().Recipe(File, recipe.ToString());
  }

  private IReadOnlyList<string> Findings(LoadedStubGame game) {
    IReadOnlyList<string> found = ObtainabilityCheck.Run(game, "stub").Errors;
    foreach (string line in found)
      output.WriteLine(line);
    return found;
  }

  private static string Unmade(string code, string type) =>
    $"{File}: {code} ({type}, RecipeIngredient): nothing makes it";

  // Fails when Run stops reporting a recipe ingredient or a construction stage ingredient that
  // nothing makes.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void An_ingredient_nothing_makes_is_reported() =>
    Assert.Equal(
      [
        Unmade("stub:gear", "item"),
        "stub:blocktypes/rig.json: game:nails (item, ConstructionRequire): nothing makes it",
      ],
      Findings(new LoadedStubGame(
          Asking(("item", "stub:gear"), ("item", "stub:axle"))
            .Definition(
              ExBlockDef
                .Create("stub", "rig")
                .EntityBehavior(
                  "ExRightClickConstructable",
                  JObject.Parse(
                    """
                    { "stages": [ { "requireStacks": [
                      { "type": "item", "code": "nails" },
                      { "type": "item", "code": "stub:axle" }
                    ] } ] }
                    """
                  )
                )
            )
        ).Item("stub:gear").Item("stub:axle").Output(EnumItemClass.Item, "stub:axle"))
    );

  // Fails when a source stops counting: a recipe output, a smelted, crushed or ground stack, a
  // kiln firing, another type's drop, a world source, or a wildcard ingredient one made code meets.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void Each_source_makes_what_it_names() =>
    Assert.Empty(
      Findings(
        new LoadedStubGame(
          Asking(
            ("item", "stub:crafted"),
            ("item", "stub:smelted"),
            ("item", "stub:crushed"),
            ("item", "stub:ground"),
            ("block", "stub:fired"),
            ("item", "stub:dropped"),
            ("block", "game:gravel-granite"),
            ("item", "stub:any-*")
          )
        )
          .Output(EnumItemClass.Item, "stub:crafted")
          .Output(EnumItemClass.Item, "stub:any-oak")
          .Item(
            "stub:ore",
            ore => {
              ore.CombustibleProps = new CombustibleProperties {
                SmeltedStack = Stack(EnumItemClass.Item, "stub:smelted"),
              };
              ore.CrushingProps = new CrushingProperties {
                CrushedStack = Stack(EnumItemClass.Item, "stub:crushed"),
              };
              ore.GrindingProps = new GrindingProperties {
                GroundStack = Stack(EnumItemClass.Item, "stub:ground"),
              };
            }
          )
          .Block(
            "stub:raw",
            raw =>
              raw.Attributes = new JsonObject(
                JObject.Parse(
                  """
                  { "beehivekiln": { "0": { "type": "block", "code": "stub:fired" } } }
                  """
                )
              )
          )
          .Block(
            "stub:bush",
            bush =>
              bush.Drops = [
                new BlockDropItemStack
                {
                  Type = EnumItemClass.Item,
                  Code = new AssetLocation("stub:dropped"),
                },
              ]
          )
      )
    );

  private static JsonItemStack Stack(EnumItemClass type, string code) =>
    new() { Type = type, Code = new AssetLocation(code) };

  // Fails when a block's drop of its own code counts as made, or its drop of another state of
  // its own type does.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void A_blocks_drop_of_its_own_type_makes_nothing() =>
    Assert.Equal(
      [Unmade("stub:crate", "block"), Unmade("stub:barrel-pine", "block")],
      Findings(
        new LoadedStubGame(
          Asking(("block", "stub:crate"), ("block", "stub:barrel-pine"))
        )
          .Block("stub:crate", crate => crate.Drops = [Drop("stub:crate")])
          .Block(
            "stub:barrel-oak",
            barrel => barrel.Drops = [Drop("stub:barrel-pine")],
            ("wood", "oak")
          )
          .Block("stub:barrel-pine", null, ("wood", "pine"))
      )
    );

  private static BlockDropItemStack Drop(string code) =>
    new() { Type = EnumItemClass.Block, Code = new AssetLocation(code) };

  // Fails when a creative-listed block stops counting as made through another block of its type,
  // or one whose type nothing makes stops being reported.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void A_creative_block_is_made_when_a_block_of_its_type_is() =>
    Assert.Equal(
      ["stub:valve-ns (block, creative tab): nothing makes it"],
      Findings(
        new LoadedStubGame(new RecipeStubSource().Covering("stub"))
          .Output(EnumItemClass.Block, "stub:pipe-ns")
          .Block("stub:pipe-ns", null, ("side", "ns"))
          .Block("stub:pipe-we", Listed, ("side", "we"))
          .Block("stub:valve-ns", Listed, ("side", "ns"))
          .Block("stub:hidden-ns", null, ("side", "ns"))
      )
    );

  private static void Listed(Block block) =>
    block.CreativeInventoryTabs = ["general"];

  // Fails when a declaration stops counting as made, one matching nothing stops being reported, or
  // another domain's unmatched declaration is reported in this run.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void A_declared_code_is_made_and_a_declaration_matching_nothing_is_reported() {
    ExlibChecks.Produces("stub", "stub:cast-*", "planted furnace");
    ExlibChecks.Produces("stub", "stub:rolled", "planted mill");
    ExlibChecks.Produces("other", "other:rolled", "their mill");

    Assert.Equal(
      ["stub:rolled (made by planted mill): matches no loaded block or item"],
      Findings(
        new LoadedStubGame(Asking(("item", "stub:cast-plate"))).Item(
          "stub:cast-plate"
        )
      )
    );
  }

  // Fails when Produces keeps a declaration missing a part, or keeps a repeated one twice.
  [Fact]
  public void Produces_names_all_three_and_keeps_a_repeat_once() {
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Produces("stub", "", "planted mill")
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Produces("", "stub:rolled", "planted mill")
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Produces("stub", "stub:rolled", null!)
    );
    ExlibChecks.Produces("stub", "stub:rolled", "planted mill");
    ExlibChecks.Produces("stub", "stub:rolled", "planted mill");

    Assert.Single(ExlibChecks.Produced());
  }
}
