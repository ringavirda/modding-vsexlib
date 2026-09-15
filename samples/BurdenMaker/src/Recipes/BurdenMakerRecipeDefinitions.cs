using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace BurdenMaker.Recipes;

/// <summary>Code-first grid recipe for the burdenmaker, the only source of burden.</summary>
public class BurdenMakerRecipeDefinitions : IExRecipeDefProvider {
  public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
    [Burdenmaker(domain)];

  /// <summary>The burdenmaker's placed shell: twelve same-colour fired bricks in the machine's
  /// 3 x 2 floor plan.</summary>
  private static ExRecipeDef Burdenmaker(string domain) =>
    ExRecipeDef
      .Create(domain, "grid", "burdenmaker")
      .GridObject(r =>
        r.Pattern("BBB,BBB")
          .Size(3, 2)
          .Ingredient(
            "B",
            i =>
              i.Item("game:burnedbrick-*")
                .Named(
                  "brick",
                  "black",
                  "brown",
                  "cream",
                  "gray",
                  "orange",
                  "red",
                  "tan"
                )
                .Quantity(2)
          )
          .OutputBlock($"{domain}:burdenmaker-{{brick}}-n", 1)
      );
}
