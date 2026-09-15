using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Serializes every test class touching <see cref="ExpandedLib.Helpers.ExMeasure.System"/>,
/// the process-global measurement setting.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ExMeasureCollection {
  public const string Name = "ExMeasure";
}
