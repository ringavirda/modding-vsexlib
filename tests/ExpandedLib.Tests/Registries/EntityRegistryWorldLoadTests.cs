using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The domain <see cref="EntityRegistry.RegisterAll"/> records for an assembly outlives a
/// later world's load, so a world registering its classes afterwards keys them under it. The
/// assembly is emitted fresh, named apart from its modid.</summary>
public class EntityRegistryWorldLoadTests : IDisposable {
  private readonly Type _widget = EmitWidget();

  private readonly ILogger? _logger = EntityRegistry.Logger;

  private Assembly Widgets => _widget.Assembly;

  public void Dispose() {
    EntityRegistry.Logger = _logger;
    var domains =
      (IDictionary<Assembly, string>)
        typeof(EntityRegistry)
          .GetField(
            "_domainByAssembly",
            BindingFlags.NonPublic | BindingFlags.Static
          )!
          .GetValue(null)!;
    domains.Remove(Widgets);
    ExDefinitions.Clear();
  }

  // BlockEntityWidget with [BlockEntityRegister], in an assembly declaring no ExDomain.
  private static Type EmitWidget() {
    var asm = AssemblyBuilder.DefineDynamicAssembly(
      new AssemblyName($"worldloadwidgets.{Guid.NewGuid():N}"),
      AssemblyBuilderAccess.Run
    );
    TypeBuilder builder = asm.DefineDynamicModule("worldloadwidgets")
      .DefineType(
        "WorldLoad.BlockEntityWidget",
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
    return builder.CreateType();
  }

  // Fails when a world starting to load forgets the domain RegisterAll recorded: the second world
  // keys the widget under the assembly's name, and the fallback warns.
  [Fact]
  public void A_world_registering_classes_after_another_loads_keys_them_under_the_registered_modid() {
    EntityRegistry.Logger = new RecordingLogger();
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = "loadedmod" }
    );
    EntityRegistry.RegisterAll(new TestWorld().Api, mod, Widgets);

    EntityRegistry.ResetForWorld();
    var world = new TestWorld().RegisterClasses(Widgets);

    Assert.Equal(
      _widget,
      world.Api.ClassRegistry.GetBlockEntity("loadedmod.BlockEntityWidget")
    );
  }
}
