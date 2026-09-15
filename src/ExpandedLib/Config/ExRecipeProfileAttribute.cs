using System;

namespace ExpandedLib.Config;

/// <summary>Marks an <c>[ExConfigRegister]</c> config as a recipe-cost catalogue for <c>ExConfigGenerator</c>.</summary>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExRecipeProfileAttribute : Attribute {
  /// <summary>Name of the public <c>string</c> property holding the active recipe-cost level, read
  /// off this class or off <see cref="LevelConfig"/>. Defaults to <c>"RecipeLevel"</c>.</summary>
  public string RecipeLevelProperty { get; set; } = "RecipeLevel";

  /// <summary>The <c>[ExConfigRegister]</c> config that carries <see cref="RecipeLevelProperty"/>, when
  /// it is not this class. Unset means the level property lives here.</summary>
  public Type? LevelConfig { get; set; }
}
