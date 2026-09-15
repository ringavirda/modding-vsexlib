using System;
using System.Collections.Generic;
using ExpandedLib.Definitions;
using static ExpandedLib.Definitions.ExIngredients;

namespace SmokeStack.Recipes.Grid;

/// <summary>
/// Grid recipe for the smoke-stack intake, the anchor the stack column is built up from. Authored as a
/// lone recipe object: four refractory bricks around an iron collar.
/// </summary>
public class SmokeStackRecipeDefinitions : IExRecipeDefProvider {
  /// <summary>
  /// Refractory brick of any tier, capturing the tier as <c>{tier}</c> so the crafted block resolves
  /// to the matching variant.
  /// </summary>
  private static Func<IngredientBuilder, IngredientBuilder> RefractoryTier(
    int qty
  ) =>
    i =>
      i.Item("game:refractorybrick-fired-*")
        .Named("tier", "tier1", "tier2", "tier3")
        .Quantity(qty);

  public static IEnumerable<ExRecipeDef> Definitions(string domain) =>
    [
      ExRecipeDef
        .Create(domain, "grid", "smokestack")
        .GridObject(r =>
          r.Name("Smoke Stack Intake")
            .Pattern("BHB,_P_,BNB")
            .Size(3, 3)
            .Ingredient("B", RefractoryTier(4))
            .Ingredient("N", Nails(2))
            .Ingredient("H", Hammer)
            // The sample ships no pipe item of its own (siex's original recipe took a segment from
            // iiex, which this sample does not depend on); iron ingots stand in for the fitting.
            .Ingredient("P", i => i.Item("game:ingot-iron").Quantity(2))
            .OutputBlock($"{domain}:smokestack-intake-{{tier}}-n")
        ),
    ];
}
