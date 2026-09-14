using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace WidgetNamespace.Recipes;

/// <summary>A code-first grid recipe crafting one widget from four planks.</summary>
public class WidgetRecipes : IExRecipeDefProvider {
  public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
    [
      ExRecipeDef
        .Create(domain, "grid", "widget")
        .Grid(r =>
          r.Name("Widget")
            .Pattern("PP,PP")
            .Size(2, 2)
            .Ingredient("P", i => i.Item("game:plank-*").Quantity(1))
            .OutputItem("widgetdomain:widget", 1)
        ),
    ];
}
