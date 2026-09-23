#if GAME_GE_1_22
using System;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.RunsRemovalHooks"/>, the accessor's drop capture, and the
/// definitions <see cref="TestWorld.DefineBlock"/> stands up through a real class registry.</summary>
public class TestWorldDefinedBlocksTests {
  private static readonly BlockPos Pos = new(3, 0, 0);

  private static (TestWorld World, RecordingBlock Block) Recording(bool hooks) {
    var world = new TestWorld { RunsRemovalHooks = hooks };
    var block = TestBlocks.Configure(new RecordingBlock(), "test:recording", 7);
    world.Place(Pos, block);
    return (world, block);
  }

  // Fails when DoSetBlock skips the replaced block's OnBlockRemoved.
  [Fact]
  public void With_hooks_on_SetBlock_runs_the_replaced_block_removed_hook() {
    var (world, block) = Recording(hooks: true);

    world.Accessor.SetBlock(0, Pos);

    Assert.Equal(1, block.Removed);
    Assert.Same(world.Air, world.GetBlock(Pos));
  }

  // Fails when DoSetBlock ignores RunsRemovalHooks.
  [Fact]
  public void With_hooks_off_SetBlock_runs_no_removed_hook() {
    var (world, block) = Recording(hooks: false);

    world.Accessor.SetBlock(0, Pos);

    Assert.Equal(0, block.Removed);
  }

  // Fails when DoSetBlock drops the same-id check.
  [Fact]
  public void SetBlock_of_the_same_block_runs_no_removed_hook() {
    var (world, block) = Recording(hooks: true);

    world.Accessor.SetBlock(block.Id, Pos);

    Assert.Equal(0, block.Removed);
  }

  // Fails when DoBreak does not route to the block's OnBlockBroken with the player.
  [Fact]
  public void With_hooks_on_BreakBlock_runs_the_block_broken_hook_with_the_player() {
    var (world, block) = Recording(hooks: true);
    TestPlayer player = world.Player();

    world.Accessor.BreakBlock(Pos, player.Player, 0.5f);

    Assert.Same(player.Player, block.BrokenBy);
    Assert.Equal(0.5f, block.BrokenMultiplier);
  }

  // Fails when DoBreak routes to the block's OnBlockBroken regardless of RunsRemovalHooks.
  [Fact]
  public void With_hooks_off_BreakBlock_leaves_the_block_hook_unrun() {
    var (world, block) = Recording(hooks: false);

    world.Accessor.BreakBlock(Pos, world.Player().Player);

    Assert.Null(block.BrokenBy);
  }

  // Fails when DoRemoveBlockEntity neither drops the entity nor runs its hook.
  [Fact]
  public void With_hooks_on_RemoveBlockEntity_drops_the_entity_and_runs_its_hook() {
    var world = new TestWorld { RunsRemovalHooks = true };
    var be = new RemovalBe();
    world.Place(Pos, TestBlocks.Configure(new Block(), "test:holder", 8), be);

    world.Accessor.RemoveBlockEntity(Pos);

    Assert.True(be.Removed);
    Assert.Null(world.GetBlockEntity(Pos));
  }

  // Fails when DoRemoveBlockEntity ignores RunsRemovalHooks.
  [Fact]
  public void With_hooks_off_RemoveBlockEntity_leaves_the_entity() {
    var world = new TestWorld();
    var be = new RemovalBe();
    world.Place(Pos, TestBlocks.Configure(new Block(), "test:holder", 8), be);

    world.Accessor.RemoveBlockEntity(Pos);

    Assert.False(be.Removed);
    Assert.Same(be, world.GetBlockEntity(Pos));
  }

  // Fails when the BlockPos overload of SpawnItemEntity is not captured.
  [Fact]
  public void SpawnItemEntity_at_a_block_position_lands_in_Drops() {
    var world = new TestWorld();
    var stack = new ItemStack(world.RegisterItem("game:stick"));

    world.World.SpawnItemEntity(stack, Pos, null);

    Assert.Same(stack, Assert.Single(world.Drops));
  }

  // Fails when DefineBlock skips solveByType (every variant keeps the unresolved map and
  // placeholder) or leaves the drops unresolved.
  [Fact]
  public void DefineBlock_resolves_by_type_keys_and_placeholders_per_variant() {
    var world = new TestWorld().RegisterClasses(
      typeof(BlockStructureFiller).Assembly
    );
    ExBlockDef def = ExBlockDef
      .Create("test", "plate")
      .VariantGroup("metal", "iron", "copper")
      .RootKeyByType("resistanceByType", "*-iron", 7)
      .RootKeyByType("resistanceByType", "*", 2)
      .Attribute("texture", "plate-{metal}");

    Block[] blocks =
    [
      .. DefinitionCodes.Expand(def).Select(v => world.DefineBlock(def, v)),
    ];

    Assert.Equal(
      ["test:plate-iron", "test:plate-copper"],
      blocks.Select(b => b.Code.ToString())
    );
    Assert.Equal([7f, 2f], blocks.Select(b => b.Resistance));
    Assert.Equal("plate-copper", blocks[1].Attributes["texture"].AsString());
    Assert.Equal("copper", blocks[1].Variant["metal"]);
    Assert.Same(blocks[1], world.World.GetBlock(blocks[1].Id));
    Assert.Same(blocks[1], blocks[1].Drops.Single().ResolvedItemstack?.Block);
  }

  // Fails when CreateRegisteredBlockEntity skips CreateBehaviors.
  [Fact]
  public void An_entity_spawned_from_the_registry_carries_its_declared_behaviours() {
    var world = new TestWorld()
      .RegisterClass("test-plain", typeof(PlainBe))
      .RegisterClass("test-marker", typeof(MarkerBehavior));
    ExBlockDef def = ExBlockDef
      .Create("test", "marked")
      .EntityClass("test-plain")
      .EntityBehavior("test-marker");
    Block block = world.DefineBlock(def, DefinitionCodes.Expand(def).Single());

    world.Accessor.SetBlock(block.Id, Pos);

    var be = Assert.IsType<PlainBe>(world.GetBlockEntity(Pos));
    Assert.NotNull(be.GetBehavior<MarkerBehavior>());
  }

  // Fails when DefineBlock drops its registry check and reaches CreateBlock on a substitute.
  [Fact]
  public void DefineBlock_before_RegisterClasses_throws() {
    var world = new TestWorld();
    ExBlockDef def = ExBlockDef.Create("test", "early");

    Assert.Throws<InvalidOperationException>(() =>
      world.DefineBlock(def, DefinitionCodes.Expand(def).Single())
    );
  }

  // Fails when RegisterClass accepts a type that is none of the four kinds.
  [Fact]
  public void RegisterClass_of_a_non_block_type_throws() {
    Assert.Throws<ArgumentException>(() =>
      new TestWorld().RegisterClass("test-string", typeof(string))
    );
  }

  // Fails when RegisterClasses drops the IsRegistrable filter: this assembly registers items and
  // entities, which RegisterClass refuses.
  [Fact]
  public void RegisterClasses_skips_registered_classes_of_other_kinds() {
    Assembly asm = typeof(TestWorldDefinedBlocksTests).Assembly;
    var world = new TestWorld().RegisterClasses(asm);

    string key = EntityRegistry.KeyFor(
      EntityRegistry.DomainOf(asm, asm.GetName().Name ?? ""),
      typeof(RegisteredBe)
    );
    Assert.Equal(typeof(RegisteredBe), world.Api.ClassRegistry.GetBlockEntity(key));
  }

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
    public bool Removed;

    public override void OnBlockRemoved() {
      Removed = true;
      base.OnBlockRemoved();
    }
  }

  private sealed class PlainBe : BlockEntity { }

  [BlockEntityRegister]
  private sealed class RegisteredBe : BlockEntity { }

  private sealed class MarkerBehavior(BlockEntity be) : BlockEntityBehavior(be);
}
#endif
