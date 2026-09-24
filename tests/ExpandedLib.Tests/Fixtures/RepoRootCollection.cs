using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Serializes every test class that sets
/// <see cref="Testing.DefinitionGoldens.RepoRootOverride"/>, the process-global repository
/// root.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class RepoRootCollection {
  public const string Name = "RepoRoot";
}
