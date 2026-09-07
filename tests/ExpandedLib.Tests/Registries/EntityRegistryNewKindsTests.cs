using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The three registration kinds added to <see cref="EntityRegistry.RegisterAll"/>: entity classes,
/// entity behaviours and crop behaviours, registered the same way as the six the rung already covered
/// - same key convention, same base-type validation.
/// </summary>
public class EntityRegistryNewKindsTests : IDisposable {
  public void Dispose() {
    var field = typeof(EntityRegistry).GetField(
      "_domainByAssembly",
      BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var map = (Dictionary<Assembly, string>)field.GetValue(null)!;
    map.Remove(typeof(EntityRegistryNewKindsTests).Assembly);
    ExDefinitions.Clear();
  }

  [EntityRegister]
  private sealed class TestEntity : Entity { }

  [EntityBehaviorRegister]
  private sealed class TestEntityBehavior(Entity entity)
    : EntityBehavior(entity) {
    public override string PropertyName() => "testentitybehavior";
  }

  [CropBehaviorRegister]
  private sealed class TestCropBehavior(Block block) : CropBehavior(block);

  private static Mod FakeMod() {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = "testmod" }
    );
    return mod;
  }

  [Fact]
  public void An_EntityRegister_class_registers_under_the_convention_key() {
    var world = new TestWorld();

    EntityRegistry.RegisterAll(world.Api, FakeMod(), GetType().Assembly);

    world
      .Api.Received()
      .RegisterEntity("testmod.TestEntity", typeof(TestEntity));
  }

  [Fact]
  public void An_EntityBehaviorRegister_class_registers_under_the_convention_key() {
    var world = new TestWorld();

    EntityRegistry.RegisterAll(world.Api, FakeMod(), GetType().Assembly);

    world
      .Api.Received()
      .RegisterEntityBehaviorClass(
        "testmod.TestEntityBehavior",
        typeof(TestEntityBehavior)
      );
  }

  [Fact]
  public void A_CropBehaviorRegister_class_registers_under_the_convention_key() {
    var world = new TestWorld();

    EntityRegistry.RegisterAll(world.Api, FakeMod(), GetType().Assembly);

    world
      .Api.Received()
      .RegisterCropBehavior(
        "testmod.TestCropBehavior",
        typeof(TestCropBehavior)
      );
  }
}
