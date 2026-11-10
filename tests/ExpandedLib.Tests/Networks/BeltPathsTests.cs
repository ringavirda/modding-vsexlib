using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The server's record of strung belts' cells and the placed-block hook that tells the owner
/// (<see cref="BeltPaths"/>).
/// </summary>
public class BeltPathsTests {
  private static readonly Block Slab = TestBlocks.Configure(
    new Block { CollisionBoxes = [new Cuboidf(0f, 0f, 0.55f, 1f, 1f, 1f)] },
    "test:slab",
    71
  );

  private static readonly Block Grass = TestBlocks.Configure(
    new Block { CollisionBoxes = null },
    "test:grass",
    73
  );

  private static readonly BlockPos Crossed = new(2, 1, 0, 0);

  private static BeltPulley Pulley(int x, int y, int z) =>
    new(new BlockPos(x, y, z, 0), EnumAxis.Z, 1);

  private class Owner(BeltPulley? from, BeltPulley? to) : IBeltPathOwner {
    public readonly List<BlockPos> Blocked = [];

    public BlockPos Pos => From?.Cell ?? new BlockPos(0, 0, 0, 0);

    public BeltPulley? From { get; } = from;

    public BeltPulley? To { get; } = to;

    public IReadOnlyCollection<BlockPos> ClaimedCells => [];

    public void PathBlocked(BlockPos pos) => Blocked.Add(pos);
  }

  private sealed class OwnerEntity(BeltPulley from, BeltPulley to)
    : BlockEntity,
      IBeltPathOwner {
    public readonly List<BlockPos> Blocked = [];

    BlockPos IBeltPathOwner.Pos => from.Cell;

    public BeltPulley? From => from;

    public BeltPulley? To => to;

    public IReadOnlyCollection<BlockPos> ClaimedCells => [];

    public void PathBlocked(BlockPos pos) => Blocked.Add(pos);
  }

  private static Owner Slant() => new(Pulley(0, 0, 0), Pulley(4, 1, 0));

  private static void Place(TestWorld world, BlockPos pos, Block block) {
    world.Place(pos, block);
    IServerPlayer player = Substitute.For<IServerPlayer>();
    world.Api.Event.DidPlaceBlock += Raise.Event<BlockPlacedDelegate>(
      player,
      0,
      new BlockSelection { Position = pos.Copy() },
      null
    );
  }

  [Fact]
  public void Crossing_belt_refused() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    var first = Slant();
    paths.Claim(first);

    Assert.Same(first, paths.OwnerAt(Crossed));
    Assert.Equal(
      BeltPath.CrossBelt,
      paths.Check(Pulley(1, -1, 0), Pulley(3, 3, 0), [])
    );
    Assert.Null(paths.Check(Pulley(1, -1, 0), Pulley(3, 3, 0), [], first));
  }

  [Fact]
  public void Second_belt_through_the_loop_refused() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    var first = new Owner(Pulley(0, 0, 0), Pulley(4, 0, 0));
    paths.Claim(first);

    Assert.Same(first, paths.OwnerAt(new BlockPos(2, 0, 0, 0)));
    Assert.Equal(
      BeltPath.CrossBelt,
      paths.Check(Pulley(1, -2, 0), Pulley(3, 2, 0), [])
    );
  }

  [Fact]
  public void A_released_belt_claims_nothing() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    var first = Slant();
    paths.Claim(first);

    paths.Release(first);

    Assert.Null(paths.OwnerAt(Crossed));
    Assert.Null(paths.Check(Pulley(1, -1, 0), Pulley(3, 3, 0), []));
  }

  [Fact]
  public void Claiming_again_replaces_the_cells_the_owner_held() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    paths.Claim(Slant());

    paths.Claim(new Owner(null, null));

    Assert.Null(paths.OwnerAt(Crossed));
  }

  [Fact]
  public void Block_placed_in_path_calls_owner() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    var owner = Slant();
    paths.Claim(owner);

    Place(world, Crossed, Slab);

    Assert.Equal([Crossed], owner.Blocked);
  }

  [Fact]
  public void Grass_placed_in_path_leaves_the_belt() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    var owner = Slant();
    paths.Claim(owner);

    Place(world, Crossed, Grass);

    Assert.Empty(owner.Blocked);
  }

  [Fact]
  public void Registry_rebuilt_after_load() {
    var world = new TestWorld();
    var paths = BeltPaths.Start(world.Api);
    var chunk = Substitute.For<IWorldChunk>();
    var saved = new OwnerEntity(Pulley(0, 0, 0), Pulley(4, 1, 0));
    chunk.BlockEntities.Returns(
      new Dictionary<BlockPos, BlockEntity> { [saved.From!.Value.Cell] = saved }
    );

    world.Api.Event.ChunkColumnLoaded += Raise.Event<ChunkColumnLoadedDelegate>(
      new Vec2i(0, 0),
      new[] { chunk }
    );

    Assert.Same(saved, paths.OwnerAt(Crossed));
    Assert.Equal(
      BeltPath.CrossBelt,
      paths.Check(Pulley(1, -1, 0), Pulley(3, 3, 0), [])
    );
  }

  [Fact]
  public void Of_finds_the_started_record() {
    var world = new TestWorld();

    Assert.Null(BeltPaths.Of(world.Api));
    Assert.Same(BeltPaths.Start(world.Api), BeltPaths.Of(world.Api));
  }
}
