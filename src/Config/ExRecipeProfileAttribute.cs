using System;

namespace ExpandedLib.Config;

/// <summary>
/// Added alongside <c>[ExConfigRegister]</c> on a recipe-cost catalogue config to have the
/// <c>ExConfigGenerator</c> also emit that mod's <see cref="ExpandedLib.Registries.RecipeProfile"/>
/// registration, folded into the generated accessor's <c>Load(ICoreAPI)</c>. The config needs a
/// <c>Dictionary&lt;string, RecipeCostEntry&gt;</c> catalogue property, a matching static
/// <c>DefaultCatalogue()</c> method and a <c>string</c> level property (see
/// <see cref="RecipeLevelProperty"/>); <c>Code</c> is the mod id already passed to
/// <c>[ExConfigRegister]</c>. See wiki/Recipe-Costs.md for a worked example.
/// </summary>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExRecipeProfileAttribute : Attribute {
  /// <summary>Name of the config's public <c>string</c> property holding the active recipe-cost
  /// level. Defaults to <c>"RecipeLevel"</c>, the config's only property of that role; set this when
  /// a config names it differently.</summary>
  public string RecipeLevelProperty { get; set; } = "RecipeLevel";
}
