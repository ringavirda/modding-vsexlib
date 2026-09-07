using System.Collections.Generic;
using ExpandedLib.Config;

namespace ExpandedLib.Tests;

/// <summary>
/// A minimal <c>[ExConfigRegister]</c> + <c>[ExRecipeProfile]</c> config, top-level so
/// <c>ExConfigGenerator</c> emits a proper accessor for it, exercising the generated
/// <c>RecipeProfile</c> registration folded into <c>Load</c> - see <see cref="ExConfigGeneratorTests"/>.
/// </summary>
[ExConfigRegister("exrecipeprofiletest.json", "exlib-recipeprofile-test")]
[ExRecipeProfile]
public class ExRecipeProfileTestConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  public string RecipeLevel { get; set; } = "normal";

  public Dictionary<string, ExpandedLib.Registries.RecipeCostEntry> Recipes { get; set; } =
    new();

  public static Dictionary<string, ExpandedLib.Registries.RecipeCostEntry> DefaultCatalogue() =>
    new();
}
