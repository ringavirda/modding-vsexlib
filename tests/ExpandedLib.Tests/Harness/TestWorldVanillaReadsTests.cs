using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>What vanilla blocks and behaviours read off the test world answers as the engine's
/// does: the typed block-entity read, the player's world-data controls, a started vanilla mod
/// system, and behaviour factories beside a real class registry.</summary>
public class TestWorldVanillaReadsTests {
  private static BlockPos Cell => new(2, 3, 4);

  #region Typed block-entity read

  // Fails when the accessor leaves GetBlockEntity<T> to the substitute's default.
  [Fact]
  public void The_typed_read_answers_the_cells_entity() {
    var (world, be) = WithEntity();

    Assert.Same(be, world.Accessor.GetBlockEntity<PlainBe>(Cell));
    Assert.Same(be, world.Accessor.GetBlockEntity<BlockEntity>(Cell));
  }

  // Fails when the typed read returns the entity without checking it is a T.
  [Fact]
  public void The_typed_read_of_another_type_is_null() {
    var (world, _) = WithEntity();

    Assert.Null(world.Accessor.GetBlockEntity<OtherBe>(Cell));
    Assert.Null(world.Accessor.GetBlockEntity<PlainBe>(Cell.AddCopy(1, 0, 0)));
  }

  // Fails when the typed read ignores the chunk being away.
  [Fact]
  public void The_typed_read_of_an_unloaded_cell_is_null() {
    var (world, _) = WithEntity();

    world.UnloadChunkAt(Cell);

    Assert.Null(world.Accessor.GetBlockEntity<PlainBe>(Cell));
  }

  #endregion

  #region Player world-data controls

  // Fails when TestPlayer leaves WorldData.EntityControls unset.
  [Fact]
  public void WorldData_controls_report_the_keys_Interact_holds() {
    var world = new TestWorld();
    var keys = new KeyReader();
    world.Place(Cell, TestBlocks.Configure(keys, "test:keys", 61));
    TestPlayer player = world.Player();

    player.Interact(Cell, sneak: true, ctrl: true);

    Assert.Same(player.Entity.Controls, player.Player.WorldData.EntityControls);
    Assert.True(keys.Shift);
    Assert.True(keys.Ctrl);
    Assert.False(player.Player.WorldData.EntityControls.ShiftKey);
  }

  #endregion

  #region Started mod systems

  // Fails when StartModSystem skips Register (GetModSystem answers null) or skips Start (the
  // reinforcement system reads a null api).
  [Fact]
  public void A_started_reinforcement_system_answers_a_lockable_block() {
    var world = new TestWorld();
    var block = TestBlocks.Configure(new Block(), "test:lockable", 62);
    var lockable = new BlockBehaviorLockable(block);
    block.BlockBehaviors = [lockable];
    block.CollectibleBehaviors = [lockable];
    world.Place(Cell, block);

    ModSystemBlockReinforcement system =
      world.StartModSystem<ModSystemBlockReinforcement>();

    Assert.Same(
      system,
      world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()
    );
    Assert.False(system.IsLockedForInteract(Cell, world.Player().Player));
    Assert.Null(Record.Exception(() => world.Player().Interact(Cell)));
  }

  #endregion

  #region Behaviour factories beside a real class registry

  // Fails when the registry builds the behaviour itself, or reads no class for a factory name.
  [Fact]
  public void A_factory_builds_the_behaviour_a_registry_entity_declares() {
    var world = new TestWorld().RegisterClass("test-plain", typeof(PlainBe));
    MarkerBehavior? built = null;
    world.RegisterBlockEntityBehaviorFactory(
      "test-made",
      be => built = new MarkerBehavior(be)
    );

    world.Accessor.SetBlock(Declaring("test-made", world).Id, Cell);

    Assert.NotNull(built);
    Assert.Same(
      built,
      world.GetBlockEntity(Cell)!.GetBehavior<MarkerBehavior>()
    );
  }

  // Fails when a factory registered before the real registry exists stays on the substitute.
  [Fact]
  public void A_factory_registered_before_the_registry_still_builds() {
    var world = new TestWorld();
    MarkerBehavior? built = null;
    world.RegisterBlockEntityBehaviorFactory(
      "test-made",
      be => built = new MarkerBehavior(be)
    );
    world.RegisterClass("test-plain", typeof(PlainBe));

    world.Accessor.SetBlock(Declaring("test-made", world).Id, Cell);

    Assert.Same(
      built,
      world.GetBlockEntity(Cell)!.GetBehavior<MarkerBehavior>()
    );
  }

  // Fails when the registry's own class wins over a factory of the same name.
  [Fact]
  public void A_factory_wins_over_a_class_of_the_same_name() {
    var world = new TestWorld()
      .RegisterClass("test-plain", typeof(PlainBe))
      .RegisterClass("test-made", typeof(OtherBehavior));
    world.RegisterBlockEntityBehaviorFactory(
      "test-made",
      be => new MarkerBehavior(be)
    );

    world.Accessor.SetBlock(Declaring("test-made", world).Id, Cell);

    BlockEntity be = world.GetBlockEntity(Cell)!;
    Assert.NotNull(be.GetBehavior<MarkerBehavior>());
    Assert.Null(be.GetBehavior<OtherBehavior>());
  }

  private static Block Declaring(string behavior, TestWorld world) {
    Block block = TestBlocks.Configure(new Block(), "test:declaring", 63);
    block.EntityClass = "test-plain";
    block.BlockEntityBehaviors =
    [
      new BlockEntityBehaviorType { Name = behavior },
    ];
    world.Register(block);
    return block;
  }

  #endregion

  private static (TestWorld, PlainBe) WithEntity() {
    var world = new TestWorld();
    var be = new PlainBe();
    world.Place(Cell, TestBlocks.Configure(new Block(), "test:plain", 60), be);
    return (world, be);
  }

  private sealed class PlainBe : BlockEntity { }

  private sealed class OtherBe : BlockEntity { }

  private sealed class MarkerBehavior(BlockEntity be) : BlockEntityBehavior(be);

  private sealed class OtherBehavior(BlockEntity be) : BlockEntityBehavior(be);

  private sealed class KeyReader : Block {
    public bool Shift;
    public bool Ctrl;

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      Shift = byPlayer.WorldData.EntityControls.ShiftKey;
      Ctrl = byPlayer.WorldData.EntityControls.CtrlKey;
      return true;
    }
  }
}
