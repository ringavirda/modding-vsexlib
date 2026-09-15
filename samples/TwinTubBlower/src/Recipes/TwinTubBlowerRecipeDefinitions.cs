using System.Collections.Generic;
using ExpandedLib.Definitions;
using static ExpandedLib.Definitions.ExIngredients;

namespace TwinTubBlower.Recipes;

/// <summary>Code-first grid recipe for the blower: two leather-topped wooden tubs on a nailed
/// frame, driven by a vanilla wood axle.</summary>
public class TwinTubBlowerRecipeDefinitions : IExRecipeDefProvider {
  public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
    [
      ExRecipeDef
        .Create(domain, "grid", "twintubblower")
        .Grid(r =>
          r.Name("Twin Tub Blower")
            .Pattern("LPL,PNP,_H_")
            .Size(3, 3)
            // No concrete `game:leather` item exists; must use the fully qualified variant code.
            .Ingredient(
              "L",
              i => i.Item("game:leather-normal-plain").Quantity(2)
            )
            .Ingredient("P", i => i.Item("game:plank-*").Quantity(1))
            .Ingredient("N", Nails(1))
            .Ingredient("H", Hammer)
            .OutputBlock($"{domain}:blower-twintubblower-n")
        ),
    ];
}
