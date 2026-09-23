using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Saved names are primary keys; a bare key an <c>[assembly: ExPublishedSaveKeys]</c> assembly and
/// an unmarked one both claim belongs to the marked one in either order; aliases load only. Each
/// claimant is emitted into its own assembly.
/// </summary>
public class EntityRegistrySaveKeyTests : IDisposable {
  private readonly List<Assembly> _emitted = [];
  private readonly TestWorld _world = NewWorld();

  // A world whose block entity registrations land in its own class registry.
  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterClasses();
    world
      .Api.When(x =>
        x.RegisterBlockEntityClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci => world.RegisterClass(ci.ArgAt<string>(0), ci.ArgAt<Type>(1)));
    return world;
  }

  public void Dispose() {
    var domains = (Dictionary<Assembly, string>)Field("_domainByAssembly");
    var primaries = (Dictionary<Type, string>)Field("_primaryKeys");
    foreach (Assembly asm in _emitted) {
      domains.Remove(asm);
      foreach (Type type in asm.GetTypes())
        primaries.Remove(type);
    }
    ExDefinitions.Clear();
  }

  private static object Field(string name) =>
    typeof(EntityRegistry)
      .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
      .GetValue(null)!;

  // One public BlockEntity{shortId} with [BlockEntityRegister], in a fresh assembly.
  private Type Emit(string modId, string shortId, bool published) {
    var asm = AssemblyBuilder.DefineDynamicAssembly(
      new AssemblyName($"{modId}.{shortId}.{Guid.NewGuid():N}"),
      AssemblyBuilderAccess.Run
    );
    if (published)
      asm.SetCustomAttribute(
        new CustomAttributeBuilder(
          typeof(ExPublishedSaveKeysAttribute).GetConstructor(Type.EmptyTypes)!,
          []
        )
      );
    TypeBuilder builder = asm.DefineDynamicModule(modId)
      .DefineType(
        $"{modId}.BlockEntity{shortId}",
        TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
        typeof(BlockEntity)
      );
    builder.SetCustomAttribute(
      new CustomAttributeBuilder(
        typeof(BlockEntityRegisterAttribute).GetConstructor([typeof(string)])!,
        [null]
      )
    );
    builder.DefineDefaultConstructor(MethodAttributes.Public);
    Type type = builder.CreateType();
    _emitted.Add(asm);
    return type;
  }

  private void Register(string modId, Type type) =>
    Register(_world, modId, type);

  private static void Register(TestWorld world, string modId, Type type) {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = modId }
    );
    EntityRegistry.RegisterAll(world.Api, mod, type.Assembly);
  }

  private IEnumerable<string> Notifications =>
    _world
      .Log.Entries.Where(e => e.Type == EnumLogType.Notification)
      .Select(e => e.Message);

  // Fails when the primary key is registered before the aliases.
  [Fact]
  public void A_block_entity_is_saved_under_its_primary_key() {
    Type type = Emit("keysmod", "Sprocket", published: false);

    Register("keysmod", type);

    Assert.Equal(
      "keysmod.BlockEntitySprocket",
      _world.Api.ClassRegistry.GetBlockEntityClass(type)
    );
    Assert.Equal(type, _world.Api.ClassRegistry.GetBlockEntity("sprocket"));
    Assert.Equal(
      type,
      _world.Api.ClassRegistry.GetBlockEntity("keysmod.Sprocket")
    );
  }

  // Fails when the unmarked claimant registers the contested key anyway, the last registration
  // winning.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void A_marked_assembly_keeps_a_contested_bare_key(bool markedFirst) {
    Type marked = Emit("oldmod", "Gear", published: true);
    Type unmarked = Emit("newmod", "Gear", published: false);

    if (markedFirst) {
      Register("oldmod", marked);
      Register("newmod", unmarked);
    } else {
      Register("newmod", unmarked);
      Register("oldmod", marked);
    }

    Assert.Equal(marked, _world.Api.ClassRegistry.GetBlockEntity("gear"));
    Assert.Equal(marked, _world.Api.ClassRegistry.GetBlockEntity("Gear"));
    Assert.Equal(
      "oldmod.BlockEntityGear",
      _world.Api.ClassRegistry.GetBlockEntityClass(marked)
    );
    Assert.Equal(
      "newmod.BlockEntityGear",
      _world.Api.ClassRegistry.GetBlockEntityClass(unmarked)
    );
  }

  // Fails when a contested key between a marked and an unmarked claimant logs the Error.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void The_unmarked_claimant_logs_one_notification_and_no_error(
    bool markedFirst
  ) {
    Type marked = Emit("oldmod", "Flange", published: true);
    Type unmarked = Emit("newmod", "Flange", published: false);

    if (markedFirst) {
      Register("oldmod", marked);
      Register("newmod", unmarked);
    } else {
      Register("newmod", unmarked);
      Register("oldmod", marked);
    }

    Assert.Empty(_world.Log.Errors);
    string notice = Assert.Single(Notifications);
    Assert.Contains("'Flange'", notice);
    Assert.Contains("'flange'", notice);
    Assert.Contains(marked.FullName!, notice);
    Assert.Contains(unmarked.FullName!, notice);
  }

  // Fails when two marked claimants are settled like a marked and an unmarked one.
  [Fact]
  public void Two_marked_claimants_log_the_error_and_the_later_wins() {
    Type first = Emit("oldmod", "Valve", published: true);
    Type second = Emit("othermod", "Valve", published: true);

    Register("oldmod", first);
    Register("othermod", second);

    Assert.Contains(
      _world.Log.Errors,
      m =>
        m.Contains("'valve'")
        && m.Contains(first.FullName!)
        && m.Contains(second.FullName!)
    );
    Assert.Empty(Notifications);
    Assert.Equal(second, _world.Api.ClassRegistry.GetBlockEntity("valve"));
  }

  // Fails when bare-key ownership is shared across class registries: the marked type settled the
  // key in the first world, so the unmarked one is skipped in the second.
  [Fact]
  public void A_bare_key_settled_in_one_class_registry_is_free_in_another() {
    Type marked = Emit("oldmod", "Damper", published: true);
    Type unmarked = Emit("newmod", "Damper", published: false);
    Register("oldmod", marked);
    Register("newmod", unmarked);

    TestWorld second = NewWorld();
    Register(second, "newmod", unmarked);

    Assert.Equal(unmarked, second.Api.ClassRegistry.GetBlockEntity("damper"));
    Assert.Equal(unmarked, second.Api.ClassRegistry.GetBlockEntity("Damper"));
    Assert.DoesNotContain(
      second.Log.Entries,
      e => e.Type == EnumLogType.Notification
    );
  }

  // Fails when the alias is registered through the plain call, leaving it as the saved name.
  [Fact]
  public void An_alias_loads_the_type_and_leaves_the_saved_name_at_the_primary_key() {
    Type type = Emit("keysmod", "Cog", published: false);
    Register("keysmod", type);

    EntityRegistry.AliasBlockEntity(_world.Api, "oldmod.BlockEntityCog", type);

    Assert.Equal(
      type,
      _world.Api.ClassRegistry.GetBlockEntity("oldmod.BlockEntityCog")
    );
    Assert.Equal(
      "keysmod.BlockEntityCog",
      _world.Api.ClassRegistry.GetBlockEntityClass(type)
    );
  }

  // Fails when RegisterAll registers the primary key before the aliases.
  [Fact]
  public void An_alias_made_before_registration_leaves_the_saved_name_at_the_primary_key() {
    Type type = Emit("keysmod", "Pawl", published: false);

    EntityRegistry.AliasBlockEntity(_world.Api, "oldmod.BlockEntityPawl", type);
    Register("keysmod", type);

    Assert.Equal(
      type,
      _world.Api.ClassRegistry.GetBlockEntity("oldmod.BlockEntityPawl")
    );
    Assert.Equal(
      "keysmod.BlockEntityPawl",
      _world.Api.ClassRegistry.GetBlockEntityClass(type)
    );
  }

  // Fails when the base-type guard is removed.
  [Fact]
  public void An_alias_for_a_type_that_is_not_a_block_entity_throws() {
    var ex = Assert.Throws<ArgumentException>(() =>
      EntityRegistry.AliasBlockEntity(
        _world.Api,
        "oldmod.Thing",
        typeof(string)
      )
    );
    Assert.Equal("type", ex.ParamName);
    _world
      .Api.DidNotReceive()
      .RegisterBlockEntityClass(Arg.Any<string>(), typeof(string));
  }
}
