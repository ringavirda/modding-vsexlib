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

/// <summary>The accessor's drop capture, and the definitions <see cref="TestWorld.DefineBlock"/>
/// stands up through a real class registry.</summary>
public class TestWorldDefinedBlocksTests
{
  private static readonly BlockPos Pos = new(3, 0, 0);

  public TestWorldDefinedBlocksTests() => TestModDomain.Register();

  // Fails when the BlockPos overload of SpawnItemEntity is not captured.
  [Fact]
  public void SpawnItemEntity_at_a_block_position_lands_in_Drops()
  {
    var world = new TestWorld();
    var stack = new ItemStack(world.RegisterItem("game:stick"));

    world.World.SpawnItemEntity(stack, Pos, null);

    Assert.Same(stack, Assert.Single(world.Drops));
  }

  // Fails when DefineBlock skips solveByType (every variant keeps the unresolved map and
  // placeholder) or leaves the drops unresolved.
  [Fact]
  public void DefineBlock_resolves_by_type_keys_and_placeholders_per_variant()
  {
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
  public void An_entity_spawned_from_the_registry_carries_its_declared_behaviours()
  {
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
  public void DefineBlock_before_RegisterClasses_throws()
  {
    var world = new TestWorld();
    ExBlockDef def = ExBlockDef.Create("test", "early");

    Assert.Throws<InvalidOperationException>(() =>
      world.DefineBlock(def, DefinitionCodes.Expand(def).Single())
    );
  }

  // Fails when RegisterClass refuses an item or collectible behaviour class.
  [Fact]
  public void RegisterClass_takes_items_and_collectible_behaviours()
  {
    var world = new TestWorld()
      .RegisterClass("test-item", typeof(PlainItem))
      .RegisterClass("test-collectiblebh", typeof(PlainCollectibleBehavior));

    Assert.Equal(typeof(PlainItem), world.Api.ClassRegistry.GetItemClass("test-item"));
    Assert.Equal(
      typeof(PlainCollectibleBehavior),
      world.Api.ClassRegistry.GetCollectibleBehaviorClass("test-collectiblebh")
    );
  }

  // Fails when RegisterClasses skips a registered item or collectible behaviour class.
  [Fact]
  public void RegisterClasses_registers_items_and_collectible_behaviours()
  {
    Assembly asm = typeof(TestWorldDefinedBlocksTests).Assembly;
    string domain = EntityRegistry.DomainOf(asm, asm.GetName().Name ?? "");
    var world = new TestWorld().RegisterClasses(asm);

    Assert.Equal(
      typeof(RegisteredItem),
      world.Api.ClassRegistry.GetItemClass(
        EntityRegistry.KeyFor(domain, typeof(RegisteredItem))
      )
    );
    Assert.Equal(
      typeof(RegisteredCollectibleBehavior),
      world.Api.ClassRegistry.GetCollectibleBehaviorClass(
        EntityRegistry.KeyFor(domain, typeof(RegisteredCollectibleBehavior))
      )
    );
  }

  // Fails when RegisterClass accepts a type that is none of the six kinds.
  [Fact]
  public void RegisterClass_of_a_non_block_type_throws()
  {
    Assert.Throws<ArgumentException>(() =>
      new TestWorld().RegisterClass("test-string", typeof(string))
    );
  }

  // Fails when RegisterClasses drops the IsRegistrable filter: this assembly registers entities,
  // which RegisterClass refuses.
  [Fact]
  public void RegisterClasses_skips_registered_classes_of_other_kinds()
  {
    Assembly asm = typeof(TestWorldDefinedBlocksTests).Assembly;
    var world = new TestWorld().RegisterClasses(asm);

    string key = EntityRegistry.KeyFor(
      EntityRegistry.DomainOf(asm, asm.GetName().Name ?? ""),
      typeof(RegisteredBe)
    );
    Assert.Equal(
      typeof(RegisteredBe),
      world.Api.ClassRegistry.GetBlockEntity(key)
    );
  }

  private sealed class PlainBe : BlockEntity { }

  private sealed class PlainItem : Item { }

  private sealed class PlainCollectibleBehavior(CollectibleObject collectible)
    : CollectibleBehavior(collectible);

  [BlockEntityRegister]
  private sealed class RegisteredBe : BlockEntity { }

  [ItemRegister]
  private sealed class RegisteredItem : Item { }

  [CollectibleBehaviorRegister]
  private sealed class RegisteredCollectibleBehavior(CollectibleObject collectible)
    : CollectibleBehavior(collectible);

  private sealed class MarkerBehavior(BlockEntity be) : BlockEntityBehavior(be);
}
#endif
