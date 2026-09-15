using System.Collections.Generic;
using ExpandedLib.Definitions;
using static ExpandedLib.Definitions.ExIngredients;

namespace PlatedPipes.Recipes;

/// <summary>Grid recipes for the four plain plated segments: a metal plate hammered onto a
/// nailed frame.</summary>
public class PlatedPipeRecipeDefinitions : IExRecipeDefProvider {
  public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
    [
      ExRecipeDef
        .Create(domain, "grid", "pipes-plated")
        .Grid(r =>
          r.Name("Piping (Straight)")
            .Pattern("HPN")
            .Size(3, 1)
            .Ingredient("P", Plate(1))
            .Ingredient("N", Nails(1))
            .Ingredient("H", Hammer)
            .OutputBlock($"{domain}:pipe-plated-straight-ns", 2)
        )
        .Grid(r =>
          r.Name("Piping (Bend)")
            .Pattern("_H,NP,_N")
            .Size(2, 3)
            .Ingredient("P", Plate(1))
            .Ingredient("N", Nails(1))
            .Ingredient("H", Hammer)
            .OutputBlock($"{domain}:pipe-plated-bend-nw")
        )
        .Grid(r =>
          r.Name("Piping (TJunction)")
            .Pattern("_H_,NPN,_N_")
            .Size(3, 3)
            .Ingredient("P", Plate(1))
            .Ingredient("N", Nails(1))
            .Ingredient("H", Hammer)
            .OutputBlock($"{domain}:pipe-plated-tjunction-uns")
        )
        .Grid(r =>
          r.Name("Piping (XJunction)")
            .Pattern("HN_,NPN,_N_")
            .Size(3, 3)
            .Ingredient("P", Plate(1))
            .Ingredient("N", Nails(1))
            .Ingredient("H", Hammer)
            .OutputBlock($"{domain}:pipe-plated-xjunction-nswe")
        ),
    ];
}
