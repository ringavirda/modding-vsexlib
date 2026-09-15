using System.Collections.Generic;

namespace ExpandedLib.Registries;

/// <summary>One managed recipe in a mod's cost catalogue: its kind, the wildcard code that locates
/// it, and one self-contained <see cref="RecipeProfileCost"/> per cost profile.</summary>
public class RecipeCostEntry {
  /// <summary>How to locate and edit the recipe: <c>"grid"</c> or <c>"rcc"</c>.</summary>
  public string Type { get; set; } = "grid";

  /// <summary>Wildcard code matched against the grid output / RCC block code.</summary>
  public string Match { get; set; } = "";

  /// <summary>Profile name to everything that profile changes for this recipe.</summary>
  public Dictionary<string, RecipeProfileCost> Profiles { get; set; } = new();
}
