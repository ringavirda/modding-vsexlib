using System.Reflection;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="StructureFillers.CanPlace"/> falls back to "not placeable" when the shared
/// filler block (<see cref="StructureFillers.FillerCode"/>) is not registered.</summary>
public class StructureFillersTests {
  public StructureFillersTests() => ResetLoggedFlag();

  // The once-per-process flag is a static field; each test resets it.
  private static void ResetLoggedFlag() =>
    typeof(StructureFillers)
      .GetField(
        "_missingFillerLogged",
        BindingFlags.NonPublic | BindingFlags.Static
      )!
      .SetValue(null, false);

  [Fact]
  public void A_missing_filler_block_logs_an_error_once_across_two_calls() {
    var world = new TestWorld();
    var logger = Substitute.For<ILogger>();
    world.World.Logger.Returns(logger);

    // FillerCode defaults to exlib:structurefiller, which this TestWorld never registers.
    Assert.False(StructureFillers.CanPlace(world.World, []));
    Assert.False(StructureFillers.CanPlace(world.World, []));

    logger.Received(1).Error(Arg.Any<string>(), Arg.Any<object[]>());
  }
}
