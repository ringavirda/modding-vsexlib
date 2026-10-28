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

  // Fails when a world given shared tag registries binds registries of its own.
  [Fact]
  public void Worlds_given_shared_tags_read_one_tag_registry_pair()
  {
    var tags = new SharedTagRegistries();
    ExItemDef def = ExItemDef.Create("test", "tagged");
    var worlds = new[] { new TestWorld(), new TestWorld() };
    foreach (TestWorld world in worlds)
    {
      world.SharedTags = tags;
      world.RegisterClasses(typeof(BlockStructureFiller).Assembly);
      world.DefineItems([def]);
    }

    Assert.All(worlds, w => Assert.True(tags.IsBoundTo(w.Api)));
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

    Assert.Equal(
      typeof(PlainItem),
      world.Api.ClassRegistry.GetItemClass("test-item")
    );
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

  // Fails when the per-assembly cache is keyed so the second world registers nothing.
  [Fact]
  public void RegisterClasses_gives_a_second_world_the_same_keys_and_types_in_order()
  {
    Assembly asm = typeof(TestWorldDefinedBlocksTests).Assembly;
    string domain = EntityRegistry.DomainOf(asm, asm.GetName().Name ?? "");
    var first = new TestWorld().RegisterClasses(asm);
    var second = new TestWorld().RegisterClasses(asm);

    Assert.Equal(
      typeof(RegisteredBe),
      first.Api.ClassRegistry.GetBlockEntity(
        EntityRegistry.KeyFor(domain, typeof(RegisteredBe))
      )
    );
    var firstRegistered = Registered(first, asm, domain);
    Assert.Equal(firstRegistered, Registered(second, asm, domain));
    Assert.NotEmpty(firstRegistered);
    Assert.Equal(RegistrationOrder(first, asm), RegistrationOrder(second, asm));
    Assert.NotEmpty(RegistrationOrder(second, asm));
  }

  // Fails when the per-assembly cache is keyed on the assembly alone: the second world then
  // registers under the first domain.
  [Fact]
  public void RegisterClasses_registers_a_world_under_the_domain_recorded_when_it_registers()
  {
    Assembly asm = typeof(TestWorldDefinedBlocksTests).Assembly;
    try
    {
      RecordDomain("firstdomain", asm);
      new TestWorld().RegisterClasses(asm);
      RecordDomain("seconddomain", asm);
      var world = new TestWorld().RegisterClasses(asm);

      Assert.Equal(
        (Now: typeof(RegisteredBe), Stale: (Type?)null),
        (
          Now: world.Api.ClassRegistry.GetBlockEntity(
            "seconddomain.RegisteredBe"
          ),
          Stale: world.Api.ClassRegistry.GetBlockEntity(
            "firstdomain.RegisteredBe"
          )
        )
      );
    }
    finally
    {
      TestModDomain.Register();
    }
  }

  private static void RecordDomain(string id, Assembly asm)
  {
    var world = new TestWorld();
    world.Mods.Add(id, "1.0.0");
    EntityRegistry.RegisterAll(world.Api, world.Mods.GetMod(id)!, asm);
  }

  private static Type[] RegistrationOrder(TestWorld world, Assembly asm)
  {
    object classes = typeof(TestWorld)
      .GetField("_classes", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(world)!;
    var byType = (System.Collections.IDictionary)
      classes
        .GetType()
        .GetField(
          "blockEntityTypeToClassnameMapping",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        )!
        .GetValue(classes)!;
    return [.. byType.Keys.Cast<Type>().Where(t => t.Assembly == asm)];
  }

  private static (string Key, Type? Type)[] Registered(
    TestWorld world,
    Assembly asm,
    string domain
  ) =>
    [
      .. asm.GetTypes()
        .Where(t => t.GetCustomAttributes<RegisterAttribute>().Any())
        .Select(t => EntityRegistry.KeyFor(domain, t))
        .Select(k =>
          (
            k,
            world.Api.ClassRegistry.GetBlockEntity(k)
              ?? world.Api.ClassRegistry.GetItemClass(k)
              ?? world.Api.ClassRegistry.GetCollectibleBehaviorClass(k)
          )
        ),
    ];

  private sealed class PlainBe : BlockEntity { }

  private sealed class PlainItem : Item { }

  private sealed class PlainCollectibleBehavior(CollectibleObject collectible)
    : CollectibleBehavior(collectible);

  [BlockEntityRegister]
  private sealed class RegisteredBe : BlockEntity { }

  [ItemRegister]
  private sealed class RegisteredItem : Item { }

  [CollectibleBehaviorRegister]
  private sealed class RegisteredCollectibleBehavior(
    CollectibleObject collectible
  ) : CollectibleBehavior(collectible);

  private sealed class MarkerBehavior(BlockEntity be) : BlockEntityBehavior(be);
}
#endif
