#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The recipe's diagram key: <see cref="RecipeDiagram"/>, the gate's diagram mark, the
/// builder's <see cref="GridRecipeBuilder.Diagram"/> and the typed diagram slot.</summary>
public sealed class RecipeDiagramTests
{
  private const string Canal = "iiex:diagram-molten-canal";
  private const string Other = "iiex:diagram-bin";

  private static GridRecipe Recipe(string? diagram, bool consume = false)
  {
    var attributes = new JObject();
    if (diagram != null)
      attributes["diagram"] = diagram;
    if (consume)
      attributes["consumeDiagram"] = true;
    return new GridRecipe
    {
      Name = new AssetLocation("iiex:canal"),
      Attributes = new JsonObject(attributes),
    };
  }

  private static bool Allowed(GridRecipe recipe) =>
    CraftSequenceGate.Allows(new CraftSequenceRegistry(), recipe);

  // Fails when Allows ignores the recipe's diagram: a keyed recipe then matches with no mark.
  [Fact]
  public void A_diagram_recipe_is_refused_without_a_mark()
  {
    GridRecipe keyed = Recipe(Canal);

    Assert.False(Allowed(keyed));
    using (CraftSequenceGate.At("workbench"))
      Assert.False(Allowed(keyed));
    using (CraftSequenceGate.At("workbench", Canal))
      Assert.True(Allowed(keyed));
    Assert.False(Allowed(keyed));
  }

  // Fails when Allows passes a keyed recipe under any marked diagram instead of its own.
  [Fact]
  public void A_diagram_recipe_is_refused_under_another_diagram()
  {
    using (CraftSequenceGate.At("workbench", Other))
      Assert.False(Allowed(Recipe(Canal)));
    using (CraftSequenceGate.At("workbench", "IIEX:diagram-molten-canal"))
      Assert.False(Allowed(Recipe(Canal)));
  }

  // Fails when a diagram mark refuses every recipe, the unkeyed ones with it.
  [Fact]
  public void A_recipe_without_a_diagram_matches_under_any_mark()
  {
    GridRecipe plain = Recipe(null);

    Assert.True(Allowed(plain));
    using (CraftSequenceGate.At("workbench"))
      Assert.True(Allowed(plain));
    using (CraftSequenceGate.At("workbench", Canal))
      Assert.True(Allowed(plain));
    using (CraftSequenceGate.At("lathe", Other))
      Assert.True(Allowed(plain));
  }

  // Fails when the builder leaves showInCreatedBy true, or writes only one of the two attributes.
  [Fact]
  public void The_builder_writes_the_key_and_hides_the_recipe()
  {
    var kept = (JObject)
      ExRecipeDef
        .Create("iiex", "grid", "canal")
        .GridObject(r => r.Name("a").Pattern("P").Size(1, 1).Diagram(Canal))
        .ToJson();
    var spent = (JObject)
      ExRecipeDef
        .Create("iiex", "grid", "bins")
        .GridObject(r =>
          r.Name("b").Pattern("P").Size(1, 1).Diagram(Other, true)
        )
        .ToJson();

    Assert.Equal(Canal, (string?)kept["attributes"]!["diagram"]);
    Assert.False((bool)kept["attributes"]!["consumeDiagram"]!);
    Assert.False((bool)kept["showInCreatedBy"]!);
    Assert.Equal(Other, (string?)spent["attributes"]!["diagram"]);
    Assert.True((bool)spent["attributes"]!["consumeDiagram"]!);
  }

  // Fails when Consumes drops the Of check: a recipe with no diagram then consumes.
  [Fact]
  public void Only_a_keyed_recipe_consumes_its_diagram()
  {
    Assert.True(RecipeDiagram.Consumes(Recipe(Canal, consume: true)));
    Assert.False(RecipeDiagram.Consumes(Recipe(Canal)));
    Assert.False(RecipeDiagram.Consumes(Recipe(null, consume: true)));
  }

  // Fails when Ingredient marks a spent diagram as a tool, or a kept one as not.
  [Fact]
  public void A_spent_diagram_is_no_tool()
  {
    using var world = new TestWorld();
    Item diagram = world.RegisterItem(Canal);

    CraftingRecipeIngredient kept = RecipeDiagram.Ingredient(
      Recipe(Canal),
      world.World
    )!;
    CraftingRecipeIngredient spent = RecipeDiagram.Ingredient(
      Recipe(Canal, consume: true),
      world.World
    )!;

    Assert.True(kept.IsTool);
    Assert.False(spent.IsTool);
    Assert.Equal(1, spent.Quantity);
    Assert.Same(diagram, spent.ResolvedItemStack.Collectible);
    Assert.Null(RecipeDiagram.Ingredient(Recipe(null), world.World));
    Assert.Null(RecipeDiagram.Ingredient(Recipe(Other), world.World));
  }

  // Fails when IsDiagram reads the domain or the whole code instead of the path's start.
  [Fact]
  public void A_diagram_is_a_collectible_whose_path_starts_diagram()
  {
    using var world = new TestWorld();

    Assert.True(RecipeDiagram.IsDiagram(world.RegisterItem(Canal)));
    Assert.True(RecipeDiagram.IsDiagram(world.RegisterItem("game:diagram-x")));
    Assert.False(
      RecipeDiagram.IsDiagram(world.RegisterItem("iiex:ingot-diagram"))
    );
    Assert.False(RecipeDiagram.IsDiagram(null));
  }

  private static ItemSlot Held(Item item) =>
    new DummySlot(new ItemStack(item, 1));

  // Fails when Diagram() has no predicate: the slot then takes any stack.
  [Fact]
  public void The_diagram_slot_takes_only_diagrams()
  {
    using var world = new TestWorld();
    MachineSlotSpec spec = MachineSlotSpec.Diagram("#112233");

    Assert.NotNull(spec.Accepts);
    Assert.False(spec.TakeOnly);
    Assert.Equal("#112233", spec.HexBackgroundColor);
    Assert.True(spec.Accepts!(new ItemStack(world.RegisterItem(Canal), 1)));
    Assert.True(
      spec.Accepts(new ItemStack(world.RegisterItem("game:diagram-x"), 1))
    );
    Assert.False(
      spec.Accepts(new ItemStack(world.RegisterItem("game:stick"), 1))
    );
    Assert.False(spec.Accepts(null));
  }

  // Fails when MachineStationInventory ranks a typed input no higher than an input taking anything,
  // or TypedBonus drops to 1: a shift-clicked diagram then merges into the grid cell holding one.
  [Fact]
  public void A_shift_clicked_diagram_goes_to_the_typed_slot()
  {
    using var world = new TestWorld();
    Item diagram = world.RegisterItem(Canal);
    Item stick = world.RegisterItem("game:stick");
    var inventory = new MachineStationInventory(
      [
        MachineSlotSpec.AnyInput(),
        MachineSlotSpec.Output(),
        MachineSlotSpec.Diagram(),
      ],
      "test-bench",
      world.Api
    );

    Assert.Same(inventory[2], inventory.GetBestSuitedSlot(Held(diagram)).slot);
    Assert.Same(inventory[0], inventory.GetBestSuitedSlot(Held(stick)).slot);

    inventory[2].Itemstack = new ItemStack(diagram, 1);
    Assert.Same(inventory[2], inventory.GetBestSuitedSlot(Held(diagram)).slot);

    inventory[2].Itemstack = null;
    inventory[0].Itemstack = new ItemStack(diagram, 1);
    Assert.Same(inventory[2], inventory.GetBestSuitedSlot(Held(diagram)).slot);
  }

  private static GridRecipe Made(
    Item product,
    string name,
    string? diagram = null,
    int width = 1,
    int height = 1
  )
  {
    GridRecipe recipe = Recipe(diagram);
    recipe.Name = new AssetLocation(name);
    recipe.Width = width;
    recipe.Height = height;
    recipe.Output = new CraftingRecipeIngredient
    {
      Type = EnumItemClass.Item,
      Code = product.Code,
      ResolvedItemStack = new ItemStack(product, 1),
    };
    return recipe;
  }

  private static Block Station(string code, int id, bool station)
  {
    var attributes = new JObject();
    if (station)
      attributes["diagramStation"] = true;
    Block block = TestBlocks.Configure(new Block(), code, id);
    block.Attributes = new JsonObject(attributes);
    return block;
  }

  private static void Sequenced(string recipe)
  {
    CraftSequenceRegistry.Shared.Clear();
    CraftSequenceRegistry.Shared.Contribute(
      new CraftSequenceSet(
        CraftSequenceSet.CurrentSchema,
        "workbench",
        [new CraftSequence(recipe, [], [])]
      )
    );
  }

  // Fails when HideStationRecipes drops its key check: a keyed recipe with a 1x1 pattern then stays shown.
  [Fact]
  public void A_diagram_recipe_is_hidden()
  {
    using var world = new TestWorld();
    CraftSequenceRegistry.Shared.Clear();
    Item product = world.RegisterItem("iiex:plate");
    GridRecipe keyed = Made(product, "iiex:keyed", Canal);
    world.World.GridRecipes.Add(keyed);

    Assert.Equal(1, RecipeDiagram.HideStationRecipes(world.World));
    Assert.False(keyed.ShowInCreatedBy);
  }

  // Fails when HideStationRecipes drops its sequence check: a plain 1x1 recipe with a sequence then stays shown.
  [Fact]
  public void A_sequenced_recipe_is_hidden()
  {
    using var world = new TestWorld();
    Sequenced("iiex:sequenced");
    Item product = world.RegisterItem("iiex:plate");
    GridRecipe sequenced = Made(product, "iiex:sequenced");
    world.World.GridRecipes.Add(sequenced);

    Assert.Equal(1, RecipeDiagram.HideStationRecipes(world.World));
    Assert.False(sequenced.ShowInCreatedBy);
    CraftSequenceRegistry.Shared.Clear();
  }

  // Fails when HideStationRecipes drops its size check: a plain 4x1 recipe then stays shown.
  [Fact]
  public void A_recipe_wider_than_three_is_hidden()
  {
    using var world = new TestWorld();
    CraftSequenceRegistry.Shared.Clear();
    Item product = world.RegisterItem("iiex:plate");
    GridRecipe wide = Made(product, "iiex:wide", width: 4);
    GridRecipe tall = Made(product, "iiex:tall", height: 4);
    world.World.GridRecipes.Add(wide);
    world.World.GridRecipes.Add(tall);

    Assert.Equal(2, RecipeDiagram.HideStationRecipes(world.World));
    Assert.False(wide.ShowInCreatedBy);
    Assert.False(tall.ShowInCreatedBy);
  }

  // Fails when HideStationRecipes hides every recipe: a plain 3x3 recipe with no key or sequence is hidden.
  [Fact]
  public void A_hand_recipe_stays_shown()
  {
    using var world = new TestWorld();
    CraftSequenceRegistry.Shared.Clear();
    Item product = world.RegisterItem("iiex:plate");
    GridRecipe hand = Made(product, "iiex:hand", width: 3, height: 3);
    world.World.GridRecipes.Add(hand);

    Assert.Equal(0, RecipeDiagram.HideStationRecipes(world.World));
    Assert.True(hand.ShowInCreatedBy);
  }

  // Fails when MadeAt takes the station from a later block, or from any block, instead of the first
  // loaded block whose attributes carry diagramStation.
  [Fact]
  public void Made_at_names_the_station_and_the_diagram()
  {
    using var world = new TestWorld();
    Item product = world.RegisterItem("iiex:plate");
    Item diagram = world.RegisterItem(Canal);
    Block plain = Station("iiex:anvil", 901, false);
    Block bench = Station("iiex:bench", 902, true);
    Block second = Station("iiex:table", 903, true);
    world.Register(plain).Register(bench).Register(second);
    GridRecipe recipe = Made(product, "iiex:plate", Canal);
    world.World.GridRecipes.Add(recipe);
    world.World.GridRecipes.Add(
      Made(world.RegisterItem("iiex:other"), "iiex:other", Canal)
    );

    IReadOnlyList<DiagramUse> uses = RecipeDiagram.MadeAt(world.World, product);

    DiagramUse use = Assert.Single(uses);
    Assert.Same(bench, use.Station);
    Assert.Same(diagram, use.Diagram);
    Assert.Same(recipe, use.Recipe);
  }

  // Fails when Makes lists every keyed recipe, or every recipe, instead of the diagram's own.
  [Fact]
  public void Makes_lists_only_the_diagram_recipes()
  {
    using var world = new TestWorld();
    Item product = world.RegisterItem("iiex:plate");
    GridRecipe mine = Made(product, "iiex:mine", Canal);
    world.World.GridRecipes.Add(mine);
    world.World.GridRecipes.Add(Made(product, "iiex:theirs", Other));
    world.World.GridRecipes.Add(Made(product, "iiex:hand"));

    Assert.Same(mine, Assert.Single(RecipeDiagram.Makes(world.World, Canal)));
  }
}
#endif
