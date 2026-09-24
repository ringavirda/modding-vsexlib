using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
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

  // Fails when allowedVariants stop narrowing a wildcard, skipVariants stop narrowing a narrowed or
  // a plain one, or a made allowed state stops counting.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void A_wildcard_is_made_only_through_the_states_its_stack_takes() {
    var recipe = new JObject {
      ["ingredientPattern"] = "ABCD",
      ["width"] = 4,
      ["height"] = 1,
      ["ingredients"] = JObject.Parse(
        """
        {
          "A": { "type": "item", "code": "stub:any-*", "name": "wood",
            "allowedVariants": ["pine"] },
          "B": { "type": "item", "code": "stub:log-*",
            "allowedVariants": ["pine", "oak"], "skipVariants": ["oak"] },
          "C": { "type": "item", "code": "stub:bark-*", "skipVariants": ["oak"] },
          "D": { "type": "item", "code": "stub:board-*",
            "allowedVariants": ["pine", "oak"] }
        }
        """
      ),
      ["output"] = new JObject { ["type"] = "item", ["code"] = "stub:product" },
    };
    Assert.Equal(
      [
        $"{File}: stub:any-* [pine] (item, RecipeIngredient): nothing makes it",
        $"{File}: stub:log-* [pine, oak] [not oak] (item, RecipeIngredient): nothing makes it",
        $"{File}: stub:bark-* [not oak] (item, RecipeIngredient): nothing makes it",
      ],
      Findings(
        new LoadedStubGame(
          new RecipeStubSource().Recipe(File, recipe.ToString())
        )
          .Output(EnumItemClass.Item, "stub:any-oak")
          .Output(EnumItemClass.Item, "stub:log-oak")
          .Output(EnumItemClass.Item, "stub:bark-oak")
          .Output(EnumItemClass.Item, "stub:board-oak")
      )
    );
  }

  // Fails when a placeholder nothing binds stops reading as a wildcard.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void A_placeholder_nothing_binds_is_made_through_any_state() =>
    Assert.Empty(
      Findings(
        new LoadedStubGame(Asking(("item", "stub:gear-{metal}"))).Output(
          EnumItemClass.Item,
          "stub:gear-iron"
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

  // Fails when the group any one row's behaviour, block class or network node writes stops being
  // read as placement's.
  [Theory]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  [InlineData("ExOrientable", "side")]
  [InlineData("ExOrientable network", "orientation")]
  [InlineData("HorizontalOrientable", "horizontalorientation")]
  [InlineData("HorizontalOrientable", "side")]
  [InlineData("NWOrientable", "orientation")]
  [InlineData("NWOrientable", "side")]
  [InlineData("Pillar", "rotation")]
  [InlineData("Pillar axis", "axis")]
  [InlineData("OmniRotatable", "rot")]
  [InlineData("BlockStairs", "horizontalorientation")]
  [InlineData("BlockStairs", "verticalorientation")]
  [InlineData("BlockNetworkNode", "orientation")]
  public void A_creative_block_is_made_through_a_group_its_placement_writes(
    string placement,
    string group
  ) =>
    Assert.Empty(
      Findings(
        new LoadedStubGame(new RecipeStubSource().Covering("stub"))
          .Output(EnumItemClass.Block, "stub:rig-oak-a")
          .Block(
            Placing(placement),
            "stub:rig-oak-a",
            null,
            ("wood", "oak"),
            (group, "a")
          )
          .Block(
            Placing(placement),
            "stub:rig-oak-b",
            Listed,
            ("wood", "oak"),
            (group, "b")
          )
      )
    );

  // Fails when a block of the same blocktype differing in a group no placement writes (machine,
  // pipe shape, an unwritten side) makes it, or an unlisted block nothing makes is reported.
  [Fact]
  [PlantedDefect(typeof(ObtainabilityCheck), nameof(ObtainabilityCheck.Run))]
  public void A_creative_block_is_not_made_through_another_state_of_a_group_placement_leaves() =>
    Assert.Equal(
      [
        "stub:forming-lathe-north (block, creative tab): nothing makes it",
        "stub:pipe-bend-ns (block, creative tab): nothing makes it",
        "stub:crate-we (block, creative tab): nothing makes it",
      ],
      Findings(
        new LoadedStubGame(new RecipeStubSource().Covering("stub"))
          .Output(EnumItemClass.Block, "stub:forming-shear-east")
          .Output(EnumItemClass.Block, "stub:pipe-straight-we")
          .Output(EnumItemClass.Block, "stub:crate-ns")
          .Block(
            Placing("HorizontalOrientable"),
            "stub:forming-shear-east",
            null,
            ("machine", "shear"),
            ("horizontalorientation", "east")
          )
          .Block(
            Placing("HorizontalOrientable"),
            "stub:forming-lathe-north",
            Listed,
            ("machine", "lathe"),
            ("horizontalorientation", "north")
          )
          .Block(
            Placing("BlockNetworkNode"),
            "stub:pipe-straight-we",
            null,
            ("type", "straight"),
            ("orientation", "we")
          )
          .Block(
            Placing("BlockNetworkNode"),
            "stub:pipe-bend-ns",
            Listed,
            ("type", "bend"),
            ("orientation", "ns")
          )
          .Block("stub:crate-ns", null, ("side", "ns"))
          .Block("stub:crate-we", Listed, ("side", "we"))
          .Block("stub:hidden-ns", null, ("side", "ns"))
      )
    );

  // A block whose class or one behaviour writes the group placement names; "axis" configures
  // Pillar's rotationVariantCode.
  private static Block Placing(string placement) {
    Block block = placement switch {
      "BlockStairs" => new BlockStairs(),
      "BlockNetworkNode" => new StubNode(),
      _ => new Block(),
    };
    BlockBehavior? behavior = placement switch {
      "ExOrientable" => Initialized(new BlockBehaviorExOrientable(block), "{}"),
      "ExOrientable network" => Initialized(
        new BlockBehaviorExOrientable(block),
        """{ "mode": "network" }"""
      ),
      "HorizontalOrientable" => new BlockBehaviorHorizontalOrientable(block),
      "NWOrientable" => new BlockBehaviorNWOrientable(block),
      "Pillar" => new BlockBehaviorPillar(block),
      "Pillar axis" => new BlockBehaviorPillar(block) {
        propertiesAtString = """{ "rotationVariantCode": "axis" }""",
      },
      "OmniRotatable" => new BlockBehaviorOmniRotatable(block),
      _ => null,
    };
    block.BlockBehaviors = behavior == null ? [] : [behavior];
    block.CollectibleBehaviors = [.. block.BlockBehaviors];
    return block;
  }

  private static BlockBehavior Initialized(BlockBehavior behavior, string json) {
    behavior.Initialize(new JsonObject(JObject.Parse(json)));
    return behavior;
  }

  private sealed class StubNode : BlockNetworkNode {
    public override string NetworkType => "stub";
  }

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
