using System.Collections.Generic;
using ExpandedLib.Config;
using ExpandedLib.Registries;

namespace ExpandedLib.Tests;

/// <summary>A minimal <c>[ExConfigRegister]</c> + <c>[ExRecipeProfile]</c> config.
/// <c>DefaultCatalogue</c> ships one entry, distinct from the empty live
/// <see cref="Recipes"/>.</summary>
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
