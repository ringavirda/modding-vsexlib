using System;

namespace ExpandedLib.Config;

/// <summary>
/// Added alongside <c>[ExConfigRegister]</c> on a recipe-cost catalogue config to have the
/// <c>ExConfigGenerator</c> also emit that mod's <see cref="ExpandedLib.Registries.RecipeProfile"/>
/// registration, folded into the generated accessor's <c>Load(ICoreAPI)</c>. The config needs a
/// <c>Dictionary&lt;string, RecipeCostEntry&gt;</c> catalogue property and a matching static
/// <c>DefaultCatalogue()</c> method; the <c>string</c> level property (see
/// <see cref="RecipeLevelProperty"/>) lives on this same class, or on <see cref="LevelConfig"/> when a
/// mod keeps its recipe catalogue and its gameplay tunables in separate <c>[ExConfigRegister]</c>
/// configs. <c>Code</c> is the mod id already passed to <c>[ExConfigRegister]</c>. See
/// wiki/Recipe-Costs.md for a worked example.
/// </summary>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExRecipeProfileAttribute : Attribute {
  /// <summary>Name of the public <c>string</c> property holding the active recipe-cost level, read
  /// off this class or off <see cref="LevelConfig"/>. Defaults to <c>"RecipeLevel"</c>; set this when
  /// the config names it differently.</summary>
  public string RecipeLevelProperty { get; set; } = "RecipeLevel";

  /// <summary>The <c>[ExConfigRegister]</c> config that carries <see cref="RecipeLevelProperty"/>, when
  /// it is not this class. Unset (the default) means the level property lives here, alongside the
  /// catalogue; a mod that keeps its recipe catalogue in one config and toggles the level from another
  /// (e.g. a shared gameplay-tunables config) names that other config's type here.</summary>
  public Type? LevelConfig { get; set; }
}
