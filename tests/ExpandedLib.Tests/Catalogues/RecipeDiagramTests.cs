#if GAME_GE_1_22
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
}
#endif
