using System.Collections.Generic;
using ExpandedLib.Config;
using ExpandedLib.Registries;

namespace ExpandedLib.Tests;

/// <summary>
/// A <c>[ExRecipeProfile]</c> config whose level property is not named <c>RecipeLevel</c>, exercising
/// <see cref="ExRecipeProfileAttribute.RecipeLevelProperty"/> - see
/// <see cref="ExRecipeProfileWiringVariantsTests"/>.
/// </summary>
[ExConfigRegister(
  "exrecipeprofilecustomlevel.json",
  "exlib-recipeprofile-customlevel"
)]
[ExRecipeProfile(RecipeLevelProperty = "CostLevel")]
public class ExRecipeProfileCustomLevelPropertyTestConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  public string CostLevel { get; set; } = "normal";

  public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();

  public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() => new();
}
