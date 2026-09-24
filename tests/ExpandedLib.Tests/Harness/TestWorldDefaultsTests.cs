using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>What a fresh <see cref="TestWorld"/> answers without being told: each API's side,
/// the client world, the air and land claims a placement reads, the break and removal hooks, and the
/// entity class check on <see cref="TestWorld.Place"/>.</summary>
public class TestWorldDefaultsTests {
  private static readonly BlockPos Pos = new(3, 0, 0);

  private static (TestWorld World, RecordingBlock Block) Recording(
    TestWorld world
  ) {
    var block = TestBlocks.Configure(new RecordingBlock(), "test:recording", 7);
    world.Place(Pos, block);
    return (world, block);
  }

  #region Sides

  // Fails when BuildWorld leaves World.Side unset.
  [Fact]
  public void The_server_world_answers_the_server_side() {
    var world = new TestWorld();

    Assert.Equal(EnumAppSide.Server, world.World.Side);
    Assert.Equal(EnumAppSide.Server, world.Api.World.Side);
  }

  // Fails when BuildClientApi leaves ClientApi.World unset.
  [Fact]
  public void The_client_api_holds_a_client_world_over_the_same_store() {
    var world = new TestWorld();
    Block block = TestBlocks.Configure(new Block(), "test:shared", 5);
    world.Place(Pos, block);

    IClientWorldAccessor client = world.ClientApi.World;

    Assert.Equal(EnumAppSide.Client, client.Side);
    Assert.Same(client, ((ICoreAPI)world.ClientApi).World);
    Assert.Same(world.Accessor, client.BlockAccessor);
    Assert.Same(block, client.BlockAccessor.GetBlock(Pos));
    Assert.Same(block, client.GetBlock(new AssetLocation("test:shared")));
    Assert.Same(world.Log, client.Logger);
  }

  #endregion

  #region Placement

  // Fails when the harness air keeps Block's default Replaceable of 0.
  [Fact]
  public void Air_is_replaceable_by_a_solid_block_as_in_the_game() {
    var world = new TestWorld();
    Block solid = TestBlocks.Configure(new Block(), "test:solid", 5);

    Assert.Equal(9999, world.Air.Replaceable);
    Assert.True(world.Air.IsReplacableBy(solid));
  }

  // Fails when BuildWorld leaves Claims to NSubstitute's default false.
  [Fact]
  public void The_world_grants_every_player_build_access_everywhere() {
    var world = new TestWorld();

    Assert.True(
      world.World.Claims.TryAccess(
        world.Player().Player,
        Pos,
        EnumBlockAccessFlags.BuildOrBreak
      )
    );
  }

  #endregion

  #region Break hooks

  // Fails when BreakRunsBlockHooks defaults to false.
  [Fact]
  public void By_default_BreakBlock_runs_the_block_broken_hook_with_the_player() {
    var (world, block) = Recording(new TestWorld());
    TestPlayer player = world.Player();

    world.Accessor.BreakBlock(Pos, player.Player, 0.5f);

    Assert.Same(player.Player, block.BrokenBy);
    Assert.Equal(0.5f, block.BrokenMultiplier);
  }

  // Fails when DoBreak routes to the block's OnBlockBroken regardless of BreakRunsBlockHooks.
  [Fact]
  public void With_break_hooks_off_BreakBlock_tears_down_the_entity_alone() {
    var (world, block) = Recording(
      new TestWorld { BreakRunsBlockHooks = false }
    );
    var be = new RemovalBe();
    world.Place(Pos, block, be);

    world.Accessor.BreakBlock(Pos, world.Player().Player);

    Assert.Null(block.BrokenBy);
    Assert.True(be.Broken);
    Assert.True(be.Removed);
    Assert.Null(world.GetBlockEntity(Pos));
    Assert.Same(world.Air, world.GetBlock(Pos));
  }

  #endregion

  #region Removal hooks

  // Fails when RunsRemovalHooks defaults to false.
  [Fact]
  public void By_default_SetBlock_runs_the_replaced_block_removed_hook() {
    var (world, block) = Recording(new TestWorld());

    world.Accessor.SetBlock(0, Pos);

    Assert.Equal(1, block.Removed);
    Assert.Same(world.Air, world.GetBlock(Pos));
  }

  // Fails when DoSetBlock ignores RunsRemovalHooks.
  [Fact]
  public void With_removal_hooks_off_SetBlock_runs_no_removed_hook() {
    var (world, block) = Recording(new TestWorld { RunsRemovalHooks = false });

    world.Accessor.SetBlock(0, Pos);

    Assert.Equal(0, block.Removed);
  }

  // Fails when DoSetBlock drops the same-id check.
  [Fact]
  public void SetBlock_of_the_same_block_runs_no_removed_hook() {
    var (world, block) = Recording(new TestWorld());

    world.Accessor.SetBlock(block.Id, Pos);

    Assert.Equal(0, block.Removed);
  }

  // Fails when RunsRemovalHooks defaults to false.
  [Fact]
  public void By_default_RemoveBlockEntity_drops_the_entity_and_runs_its_hook() {
    var world = new TestWorld();
    var be = new RemovalBe();
    world.Place(Pos, TestBlocks.Configure(new Block(), "test:holder", 8), be);

    world.Accessor.RemoveBlockEntity(Pos);

    Assert.True(be.Removed);
    Assert.Null(world.GetBlockEntity(Pos));
  }

  // Fails when DoRemoveBlockEntity ignores RunsRemovalHooks.
  [Fact]
  public void With_removal_hooks_off_RemoveBlockEntity_leaves_the_entity() {
    var world = new TestWorld { RunsRemovalHooks = false };
    var be = new RemovalBe();
    world.Place(Pos, TestBlocks.Configure(new Block(), "test:holder", 8), be);

    world.Accessor.RemoveBlockEntity(Pos);

    Assert.False(be.Removed);
    Assert.Same(be, world.GetBlockEntity(Pos));
  }

  #endregion

  #region Entity class check

  private static TestWorld Registered() =>
    new TestWorld().RegisterClass("test-registered", typeof(RegisteredBe));

  private static Block Naming(string? entityClass) {
    Block block = TestBlocks.Configure(new Block(), "test:named", 9);
    block.EntityClass = entityClass;
    return block;
  }

  // Fails when Place stops logging the mismatch.
  [Fact]
  public void An_entity_placed_under_another_class_logs_a_warning() {
    TestWorld world = Registered();
    world.Log.Expect(EnumLogType.Warning, "names entity class 'test-other'");

    world.Place(Pos, Naming("test-other"), new RegisteredBe());

    string warning = Assert.Single(world.Log.Warnings);
    Assert.Contains("'test-other'", warning);
    Assert.Contains("'test-registered'", warning);
  }

  // Fails when the check compares anything but the registered class name.
  [Fact]
  public void An_entity_placed_under_its_own_class_logs_nothing() {
    TestWorld world = Registered();

    world.Place(Pos, Naming("test-registered"), new RegisteredBe());

    Assert.Empty(world.Log.Warnings);
  }

  // Fails when the check logs for an entity type the registry does not hold.
  [Fact]
  public void An_unregistered_entity_type_logs_nothing() {
    TestWorld world = Registered();

    world.Place(Pos, Naming("test-other"), new RemovalBe());

    Assert.Empty(world.Log.Warnings);
  }

  // Fails when the check runs for a block that names no entity class.
  [Fact]
  public void A_block_naming_no_entity_class_logs_nothing() {
    TestWorld world = Registered();

    world.Place(Pos, Naming(null), new RegisteredBe());

    Assert.Empty(world.Log.Warnings);
  }

  // Fails when the check runs in a world without a class registry.
  [Fact]
  public void A_world_without_a_class_registry_logs_nothing() {
    var world = new TestWorld();

    world.Place(Pos, Naming("test-other"), new RegisteredBe());

    Assert.Empty(world.Log.Warnings);
  }

  #endregion

  private sealed class RecordingBlock : Block {
    public int Removed;
    public IPlayer? BrokenBy;
    public float BrokenMultiplier;

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) {
      Removed++;
      base.OnBlockRemoved(world, pos);
    }

    public override void OnBlockBroken(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) {
      BrokenBy = byPlayer;
      BrokenMultiplier = dropQuantityMultiplier;
    }
  }

  private sealed class RemovalBe : BlockEntity {
    public bool Broken;
    public bool Removed;

    public override void OnBlockBroken(IPlayer? byPlayer = null) {
      Broken = true;
      base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved() {
      Removed = true;
      base.OnBlockRemoved();
    }
  }

  private sealed class RegisteredBe : BlockEntity { }
}
