#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A neighbour change at a filler cell reaches a principal that implements
/// <see cref="IFillerNeighbourTarget"/>, on the server only.</summary>
public class FillerNeighbourTests
{
  private static readonly BlockPos PrincipalPos = new(4, 4, 4);
  private static readonly BlockPos EastCell = new(5, 4, 4);
  private static readonly BlockPos Neighbour = new(5, 3, 4);

  #region Forwarding

  // Fails when the filler stops forwarding the change to its principal.
  [Fact]
  public void The_principal_gets_the_three_positions()
  {
    var (world, filler, calls) = NewWorld(
      new WatchingBlock(),
      EnumAppSide.Server
    );

    filler.OnNeighbourBlockChange(world.World, EastCell, Neighbour);

    var call = Assert.Single(calls);
    Assert.Equal((PrincipalPos, EastCell, Neighbour), call);
  }

  // Fails when the filler casts the principal to the target without checking it implements it.
  [Fact]
  public void A_principal_without_the_contract_gets_nothing_and_nothing_throws()
  {
    var (world, filler, _) = NewWorld(new PlainBlock(), EnumAppSide.Server);

    var ex = Record.Exception(() =>
      filler.OnNeighbourBlockChange(world.World, EastCell, Neighbour)
    );

    Assert.Null(ex);
  }

  // Fails when the filler drops its server-side check.
  [Fact]
  public void The_client_does_not_forward()
  {
    var (world, filler, calls) = NewWorld(
      new WatchingBlock(),
      EnumAppSide.Client
    );

    filler.OnNeighbourBlockChange(world.World, EastCell, Neighbour);

    Assert.Empty(calls);
  }

  #endregion

  #region Helpers

  private static (
    TestWorld world,
    BlockStructureFiller filler,
    List<(BlockPos, BlockPos, BlockPos)> calls
  ) NewWorld(Block principal, EnumAppSide side)
  {
    var world = new TestWorld();
    world.World.Side.Returns(side);
    var filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    world.Register(filler);
    TestBlocks.Configure(principal, "test:principal", 71);
    world.Register(principal);
    world.Place(PrincipalPos, principal);
    world.Place(
      EastCell,
      filler,
      new BlockEntityStructureFiller { Principal = PrincipalPos.Copy() }
    );
    var calls = (principal as WatchingBlock)?.Calls ?? [];
    return (world, filler, calls);
  }

  private sealed class PlainBlock : Block { }

  private sealed class WatchingBlock : Block, IFillerNeighbourTarget
  {
    public List<(BlockPos, BlockPos, BlockPos)> Calls { get; } = [];

    public void OnFillerNeighbourChanged(
      IWorldAccessor world,
      BlockPos principalPos,
      BlockPos fillerPos,
      BlockPos neighbourPos
    ) =>
      Calls.Add((principalPos.Copy(), fillerPos.Copy(), neighbourPos.Copy()));
  }

  #endregion
}
#endif
