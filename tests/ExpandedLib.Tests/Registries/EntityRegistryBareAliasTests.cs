using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The bare <c>{ShortId}</c>/<c>{shortid}</c> block-entity aliases stay process-wide and unprefixed;
/// a second type claiming an issued bare key logs an error naming both.
/// </summary>
public class EntityRegistryBareAliasTests : IDisposable {
  public void Dispose() {
    var domainField = typeof(EntityRegistry).GetField(
      "_domainByAssembly",
      BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var domainMap = (Dictionary<Assembly, string>)domainField.GetValue(null)!;
    domainMap.Remove(typeof(EntityRegistryBareAliasTests).Assembly);

    // _bareKeysIssued is process-wide; reset the two keys this fixture claims.
    var bareField = typeof(EntityRegistry).GetField(
      "_bareKeysIssued",
      BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var bareMap = (Dictionary<string, Type>)bareField.GetValue(null)!;
    bareMap.Remove("Widget");
    bareMap.Remove("widget");

    ExDefinitions.Clear();
  }

  // Two distinct types sharing one simple name both claim the bare short-id key "Widget"/"widget".
  private static class OuterA {
    [BlockEntityRegister]
    public sealed class BlockEntityWidget : BlockEntity { }
  }

  private static class OuterB {
    [BlockEntityRegister]
    public sealed class BlockEntityWidget : BlockEntity { }
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

    EntityRegistry.RegisterAll(world.Api, mod, GetType().Assembly);

    // One error per colliding alias: the exact-case "Widget" and the lower-cased "widget".
    List<string> collisions = world.Log.Errors.ToList();
    Assert.Contains(
      collisions,
      m =>
        m.Contains("'Widget'")
        && m.Contains(nameof(OuterA.BlockEntityWidget))
        && m.Contains("OuterA")
        && m.Contains("OuterB")
    );
    Assert.Contains(
      collisions,
      m =>
        m.Contains("'widget'")
        && m.Contains(nameof(OuterA.BlockEntityWidget))
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

    EntityRegistry.RegisterAll(world.Api, mod, GetType().Assembly);

    // The colliding bare keys are still (re-)issued for both types.
    world
      .Api.Received()
      .RegisterBlockEntityClass("Widget", typeof(OuterA.BlockEntityWidget));
    world
      .Api.Received()
      .RegisterBlockEntityClass("Widget", typeof(OuterB.BlockEntityWidget));
    world
      .Api.Received()
      .RegisterBlockEntityClass("widget", typeof(OuterA.BlockEntityWidget));
    world
      .Api.Received()
      .RegisterBlockEntityClass("widget", typeof(OuterB.BlockEntityWidget));
  }
}
