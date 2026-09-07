using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Every test class that registers into the process-wide <see cref="ExpandedLib.Registries.ExRecipeProfiles"/>
/// registry joins this collection, so xUnit never runs two of them at once: <see cref="ExpandedLib.Registries.ExRecipeProfiles.ApplyAll"/>
/// walks every registered profile, and a generator-driven fixture's mod id is fixed by its own
/// <c>[ExConfigRegister]</c> attribute rather than freshly random per test, so it cannot rely on
/// <c>FreshCode</c> alone to stay invisible to a concurrently running class.
/// </summary>
[CollectionDefinition(
  nameof(RecipeProfileRegistryCollection),
  DisableParallelization = true
)]
public class RecipeProfileRegistryCollection { }
