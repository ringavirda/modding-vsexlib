using System.Collections.Generic;
using ExpandedLib.Config;
using ExpandedLib.Registries;

namespace ExpandedLib.Tests;

/// <summary>
/// A minimal <c>[ExConfigRegister]</c> + <c>[ExRecipeProfile]</c> config, top-level so
/// <c>ExConfigGenerator</c> emits a proper accessor for it, exercising the generated
/// <c>RecipeProfile</c> registration folded into <c>Load</c> - see <see cref="ExRecipeProfileGeneratorTests"/>.
/// <c>DefaultCatalogue</c> ships one entry so a test can tell the generated <c>Defaults</c> delegate
/// apart from the live, still-empty <see cref="Recipes"/> catalogue.
/// </summary>
[ExConfigRegister("exrecipeprofiletest.json", "exlib-recipeprofile-test")]
[ExRecipeProfile]
public class ExRecipeProfileTestConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  public string RecipeLevel { get; set; } = "normal";

  public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();

  public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() =>
    new() {
      ["stub"] = new RecipeCostEntry { Type = "grid", Match = "exlib:stub-*" },
    };
}
