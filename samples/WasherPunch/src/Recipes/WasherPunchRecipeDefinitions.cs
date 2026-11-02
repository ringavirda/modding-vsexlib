using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace WasherPunch.Recipes;

/// <summary>Code-first grid recipe for the press: an iron screw through two plate cheeks on a plank
/// bed. The washer has none; only the press makes it.</summary>
public class WasherPunchRecipeDefinitions : IExRecipeDefProvider {
  public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
    [
      ExRecipeDef
        .Create(domain, "grid", "press")
        .Grid(r =>
          r.Name("Washer Press")
            .Pattern("_R_,IRI,PPP")
            .Size(3, 3)
            .Ingredient("R", i => i.Item("game:rod-iron").Quantity(1))
            .Ingredient("I", i => i.Item("game:metalplate-iron").Quantity(1))
            .Ingredient("P", i => i.Item("game:plank-*").Quantity(1))
            .OutputBlock($"{domain}:press")
        ),
    ];
}
