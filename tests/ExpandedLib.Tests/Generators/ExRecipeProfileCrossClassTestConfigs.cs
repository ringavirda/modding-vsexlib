using System.Collections.Generic;
using ExpandedLib.Config;
using ExpandedLib.Registries;

namespace ExpandedLib.Tests;

/// <summary>
/// Holds the recipe-cost level, separately from the catalogue that manages it - the shape both family
/// mods actually ship (a gameplay config's <c>RecipeLevel</c> alongside a dedicated recipe-catalogue
/// config's <c>Recipes</c>/<c>DefaultCatalogue</c>). See
/// <see cref="ExRecipeProfileCrossClassCatalogueTestConfig"/> and
/// <see cref="ExRecipeProfileWiringVariantsTests"/>.
/// </summary>
[ExConfigRegister(
  "exrecipeprofilecrossclasslevel.json",
  "exlib-recipeprofile-crossclass"
)]
public class ExRecipeProfileCrossClassLevelTestConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  public string RecipeLevel { get; set; } = "normal";
}

/// <summary>
/// The catalogue half of the split: <see cref="ExRecipeProfileAttribute.LevelConfig"/> points at
/// <see cref="ExRecipeProfileCrossClassLevelTestConfig"/> for <c>RecipeLevel</c>, so the generator
/// emits <c>GetLevel</c>/<c>SetLevel</c> through that config's accessor instead of this one's.
/// </summary>
[ExConfigRegister(
  "exrecipeprofilecrossclasscatalogue.json",
  "exlib-recipeprofile-crossclass"
)]
[ExRecipeProfile(
  LevelConfig = typeof(ExRecipeProfileCrossClassLevelTestConfig)
)]
public class ExRecipeProfileCrossClassCatalogueTestConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();

  public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() => new();
}
