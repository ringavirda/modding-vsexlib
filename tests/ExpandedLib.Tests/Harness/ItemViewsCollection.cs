using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Serializes the tests that load the whole game, whose asset load empties every
/// per-world registry for the process.</summary>
[CollectionDefinition("ItemViews", DisableParallelization = true)]
public class ItemViewsCollection;
