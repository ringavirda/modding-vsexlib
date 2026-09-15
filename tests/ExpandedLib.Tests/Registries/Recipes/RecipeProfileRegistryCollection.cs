using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Every test class that registers into the process-wide <see cref="ExpandedLib.Registries.ExRecipeProfiles"/>
/// registry joins this collection: xUnit never runs two of them at once.
/// </summary>
[CollectionDefinition(
  nameof(RecipeProfileRegistryCollection),
  DisableParallelization = true
)]
public class RecipeProfileRegistryCollection { }
