using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Catalogues;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>The one sound of a hand-assembly action at its cell: the anvil on a success, the tool's own sound when it has one, nothing on a refusal.</summary>
[Collection("Setups")]
public class HandAssemblySoundTests {
  private const string Anvil = "game:sounds/block/anvil";
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);

  internal static List<string> Played(TestWorld world) =>
    [
      .. world
        .World.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == "PlaySoundAt")
        .Select(c => ((AssetLocation)c.GetArguments()[0]!).ToString()),
    ];

  private static AssemblyResult? TakeOff(Scene scene) {
    scene.Player.Hold(new ItemStack(scene.Wrench));
    scene.Player.Sneaking = true;
    return HandAssemblyServer.TakeOff(
      scene.World.World,
      scene.Player.Player,
      new AssemblyTakeOff {
        X = Cell.X,
        Y = Cell.Y,
        Z = Cell.Z,
      }
    );
  }

  // Fails when a fix plays no sound, or plays it more than once.
  [Fact]
  public void Sound_FixPlaysTheAnvilOnce() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);

    Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));

    Assert.Equal([Anvil], Played(scene.World));
  }

  // Fails when a take-off plays no sound, or plays it more than once.
  [Fact]
  public void Sound_TakeOffPlaysTheAnvilOnce() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));
    scene.World.World.ClearReceivedCalls();

    Assert.Null(TakeOff(scene));

    Assert.Equal([Anvil], Played(scene.World));
  }

  // Fails when a sound is played before the refusal check, so a refused fix is heard.
  [Fact]
  public void Sound_RefusedFixPlaysNothing() {
    var scene = Scene.Standing(["ns", "we", "ne"]);
    scene.Pipe(North, "ns");
    scene.Bolts(10);

    AssemblyResult? result = scene.Fix("we", North, BlockFacing.SOUTH);

    Assert.Equal(HandAssemblyServer.NotOffered, result!.Code);
    Assert.Empty(Played(scene.World));
  }

  // Fails when a sound is played before the refusal check, so a refused take-off is heard.
  [Fact]
  public void Sound_RefusedTakeOffPlaysNothing() {
    var scene = Scene.Standing(["ns"]);

    Assert.NotNull(TakeOff(scene));

    Assert.Empty(Played(scene.World));
  }

  // Fails when the helper ignores the tool's own sound and plays the anvil for every tool.
  [Fact]
  public void Sound_AToolWithItsOwnSoundPlaysThatOne() {
    const string Own = "game:sounds/block/hammerhit";
    CraftSequenceRegistry.Shared.DefineTool(
      new CraftToolDefault("test:wrench-*", null, Own)
    );
    try {
      var scene = Scene.Standing(["ns"]);
      scene.Bolts(10);
      Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));

      Assert.Equal([Own], Played(scene.World));
    } finally {
      CraftSequenceRegistry.Shared.Clear();
    }
  }
}
