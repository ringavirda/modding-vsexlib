using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A multiblock's structure check on the client: the missing-blocks report, the build
/// outline and the in-game errors, and the outline cleared when the structure goes away.</summary>
public class MultiblockClientCheckTests {
  /// <summary>A formed structure at <paramref name="anchor"/> with its wall taken out, now on the
  /// client world.</summary>
  private static (
    TestWorld World,
    TestMegablock Machine,
    BlockPos Wall,
    int WallId
  ) Broken(BlockPos anchor) {
    var (world, machine) = NoSnowCellsTests.Formed(anchor);
    BlockPos wall = anchor.AddCopy(1, 0, 0);
    int wallId = world.Accessor.GetBlock(wall).Id;
    world.Accessor.SetBlock(0, wall);
    machine.Api = world.ClientApi;
    return (world, machine, wall, wallId);
  }

  // HighlightBlocks has a different overload per game version; this matches it by name.
  private static List<List<BlockPos>> Outlines(TestWorld world) =>
    world
      .ClientApi.World.ReceivedCalls()
      .Where(c =>
        c.GetMethodInfo().Name == nameof(IWorldAccessor.HighlightBlocks)
      )
      .Select(c => (List<BlockPos>)c.GetArguments()[2]!)
      .ToList();

  // Fails when Interact's client guard never holds on the client.
  [Fact]
  public void An_incomplete_structure_reports_and_outlines_its_missing_block() {
    var (world, machine, wall, _) = Broken(new BlockPos(5000, 10, 0));
    IPlayer player = world.Player().Player;

    machine.Interact(player);

    world
      .ClientApi.Received(1)
      .TriggerIngameError(machine, "incomplete", "missing 1");
    world.ClientApi.Received(1).ShowChatMessage(Arg.Any<string>());
    Assert.Equal([wall], Assert.Single(Outlines(world)));
  }

  // Fails when SetStructureAngle's client guard never holds on the client.
  [Fact]
  public void Turning_it_clears_the_outline_of_the_old_facing() {
    var (world, machine, _, _) = Broken(new BlockPos(5500, 10, 0));
    IPlayer player = world.Player().Player;
    machine.Interact(player);
    machine.Angle = 90;

    machine.Interact(player);

    Assert.Empty(Outlines(world)[1]);
  }

  // Fails when Interact's client guard never holds on the client.
  [Fact]
  public void Completing_it_says_so_and_clears_the_outline() {
    var (world, machine, wall, wallId) = Broken(new BlockPos(5100, 10, 0));
    IPlayer player = world.Player().Player;
    machine.Interact(player);
    world.Accessor.SetBlock(wallId, wall);

    machine.Interact(player);

    world
      .ClientApi.Received(1)
      .TriggerIngameError(machine, "complete", "complete");
    Assert.Empty(Outlines(world)[^1]);
  }

  // Fails when OnBlockRemoved's or OnBlockUnloaded's client guard never holds on the client.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Removing_or_unloading_it_clears_the_outline(bool removed) {
    var (world, machine, _, _) = Broken(
      new BlockPos(removed ? 5200 : 5300, 10, 0)
    );
    machine.Interact(world.Player().Player);

    if (removed)
      machine.OnBlockRemoved();
    else
      machine.OnBlockUnloaded();

    Assert.Equal(2, Outlines(world).Count);
    Assert.Empty(Outlines(world)[^1]);
  }

  // Fails when FromTreeAttributes' client guard never holds on the client.
  [Fact]
  public void A_sync_that_completes_it_clears_the_outline() {
    var (world, machine, _, _) = Broken(new BlockPos(5400, 10, 0));
    machine.Interact(world.Player().Player);
    var tree = new TreeAttribute();
    machine.ToTreeAttributes(tree);
    tree.SetBool("structureComplete", true);

    machine.FromTreeAttributes(tree, world.ClientApi.World);

    Assert.Equal(2, Outlines(world).Count);
    Assert.Empty(Outlines(world)[^1]);
  }
}
