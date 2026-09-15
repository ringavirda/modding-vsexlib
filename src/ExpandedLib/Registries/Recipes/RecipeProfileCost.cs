using System.Collections.Generic;

namespace ExpandedLib.Registries;

/// <summary>Everything one cost profile changes for a single recipe, self-contained. A grid recipe
/// uses <see cref="Ingredients"/> and optionally <see cref="Quantity"/>; an RCC construction uses
/// <see cref="Stages"/>.</summary>
public class RecipeProfileCost {
  /// <summary>Grid recipes: ingredient code to quantity required.</summary>
  public Dictionary<string, int>? Ingredients { get; set; }

  /// <summary>Grid recipes: the crafted output count. Null keeps the recipe's authored output.</summary>
  public int? Quantity { get; set; }

  /// <summary>RCC constructions: stage index (as a string) to that stage's require-stacks
  /// (ingredient <c>name</c>, falling back to its code, to quantity).</summary>
  public Dictionary<string, Dictionary<string, int>>? Stages { get; set; }

  /// <summary>True when this profile carries any cost data worth applying or persisting.</summary>
  public bool HasContent =>
    Quantity.HasValue
    || (Ingredients is { Count: > 0 })
    || (Stages is { Count: > 0 });
}
