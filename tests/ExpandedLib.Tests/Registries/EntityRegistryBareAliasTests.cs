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
/// The bare <c>{ShortId}</c>/<c>{shortid}</c> block-entity aliases stay unprefixed; a second type
/// claiming a bare key already issued in the same class registry logs an error naming both.
/// </summary>
public class EntityRegistryBareAliasTests : IDisposable {
  private readonly Assembly _widgets = EmitWidgets();

  private Type OuterA => _widgets.GetType("OuterA.BlockEntityWidget")!;

  private Type OuterB => _widgets.GetType("OuterB.BlockEntityWidget")!;

  public void Dispose() {
    var domainMap = (Dictionary<Assembly, string>)Field("_domainByAssembly");
    var primaries = (Dictionary<Type, string>)Field("_primaryKeys");
    domainMap.Remove(_widgets);
    foreach (Type type in _widgets.GetTypes())
      primaries.Remove(type);

    ExDefinitions.Clear();
  }

  private static object Field(string name) =>
    typeof(EntityRegistry)
      .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
      .GetValue(null)!;

  // Two distinct types sharing one simple name, OuterA.BlockEntityWidget and
  // OuterB.BlockEntityWidget, both claiming the bare short-id key "Widget"/"widget". They live in an
  // assembly of their own: the test assembly is registered whole by other classes.
  private static Assembly EmitWidgets() {
    var asm = AssemblyBuilder.DefineDynamicAssembly(
      new AssemblyName($"barealias.{Guid.NewGuid():N}"),
      AssemblyBuilderAccess.Run
    );
    ModuleBuilder module = asm.DefineDynamicModule("barealias");
    foreach (string outer in new[] { "OuterA", "OuterB" }) {
      TypeBuilder builder = module.DefineType(
        $"{outer}.BlockEntityWidget",
        TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
        typeof(BlockEntity)
      );
      builder.SetCustomAttribute(
        new CustomAttributeBuilder(
          typeof(BlockEntityRegisterAttribute).GetConstructor([
            typeof(string),
          ])!,
          [null]
        )
      );
      builder.DefineDefaultConstructor(MethodAttributes.Public);
      builder.CreateType();
    }
    return asm;
  }

  private static void ExpectCollisions(TestWorld world) {
    world.Log.Expect(EnumLogType.Error, "Bare block entity key 'Widget'");
    world.Log.Expect(EnumLogType.Error, "Bare block entity key 'widget'");
  }

  [Fact]
  public void A_second_type_claiming_an_issued_bare_key_logs_an_error_naming_both() {
    var world = new TestWorld();
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = "testmod" }
    );

    ExpectCollisions(world);
    EntityRegistry.RegisterAll(world.Api, mod, _widgets);

    // One error per colliding alias: the exact-case "Widget" and the lower-cased "widget".
    List<string> collisions = world.Log.Errors.ToList();
    Assert.Contains(
      collisions,
      m =>
        m.Contains("'Widget'")
        && m.Contains("BlockEntityWidget")
        && m.Contains("OuterA")
        && m.Contains("OuterB")
    );
    Assert.Contains(
      collisions,
      m =>
        m.Contains("'widget'")
        && m.Contains("BlockEntityWidget")
        && m.Contains("OuterA")
        && m.Contains("OuterB")
    );
  }

  [Fact]
  public void Both_claimants_still_get_every_alias_registered() {
    var world = new TestWorld();
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = "testmod" }
    );

    ExpectCollisions(world);
    EntityRegistry.RegisterAll(world.Api, mod, _widgets);

    // The colliding bare keys are still (re-)issued for both types.
    world.Api.Received().RegisterBlockEntityClass("Widget", OuterA);
    world.Api.Received().RegisterBlockEntityClass("Widget", OuterB);
    world.Api.Received().RegisterBlockEntityClass("widget", OuterA);
    world.Api.Received().RegisterBlockEntityClass("widget", OuterB);
  }
}
