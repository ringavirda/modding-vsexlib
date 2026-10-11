using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Serializes the setup scene tests, which register placers process-wide and start
/// mod systems against the shared game assemblies.</summary>
[CollectionDefinition("Setups", DisableParallelization = true)]
public class SetupsCollection;
