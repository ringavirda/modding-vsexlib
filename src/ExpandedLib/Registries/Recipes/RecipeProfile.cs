using System.Collections.Generic;

namespace ExpandedLib.Registries;

/// <summary>A mod's registration with the shared recipe-cost framework: everything
/// <see cref="ExRecipeProfiles"/> needs to read, fill, persist and apply that mod's cost
/// catalogue, plus get and set the active level.</summary>
public sealed class RecipeProfile {
  /// <summary>The mod's short code used on the command line.</summary>
  public required string Code { get; init; }

  /// <summary>The live, persisted catalogue this profile manages.</summary>
  public required System.Func<
    IDictionary<string, RecipeCostEntry>
  > Catalogue { get; init; }

  /// <summary>A fresh copy of the mod's shipped catalogue defaults.</summary>
  public required System.Func<
    IReadOnlyDictionary<string, RecipeCostEntry>
  > Defaults { get; init; }

  /// <summary>Reads the mod's currently selected cost level.</summary>
  public required System.Func<string> GetLevel { get; init; }

  /// <summary>Sets and persists the mod's selected cost level.</summary>
  public required System.Action<string> SetLevel { get; init; }

  /// <summary>Persists the catalogue file.</summary>
  public required System.Action SaveCatalogue { get; init; }

  /// <summary>The selectable level names, in display order.</summary>
  public IReadOnlyList<string> Levels { get; init; } = ["normal", "cheap"];

  /// <summary>Levels filled by scaling <c>normal</c>: level name to factor.</summary>
  public IReadOnlyDictionary<string, double> DerivedLevels { get; init; } =
    new Dictionary<string, double> { ["cheap"] = 0.5 };
}
