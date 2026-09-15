using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Serializes every test class that applies or removes a real Harmony patch. Harmony patches are
/// process-global; members: <see cref="ExHarmonyTests"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ExHarmonyCollection {
  public const string Name = "ExHarmony";
}
