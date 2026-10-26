using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Testing;
using SmokeStack.BlockEntities;
using Xunit;

namespace SmokeStack.Tests;

/// <summary>Every block entity type the sample registers loads from a tree with none of its keys and
/// initialises on both sides.</summary>
public class EmptyTreeTests {
  public static IEnumerable<object[]> Types() =>
    EmptyTreeSweep
      .BlockEntityTypes(typeof(BlockEntitySmokeStack).Assembly)
      .Select(t => new object[] { t });

  [Theory]
  [MemberData(nameof(Types))]
  public void A_registered_type_loads_and_initialises_from_an_empty_tree(
    Type type
  ) {
    IReadOnlyList<string> problems = EmptyTreeSweep.Run(
      type,
      (world, _) => world.RegisterNetwork("pipe", sys => new PipeNetwork(sys))
    );

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }
}
