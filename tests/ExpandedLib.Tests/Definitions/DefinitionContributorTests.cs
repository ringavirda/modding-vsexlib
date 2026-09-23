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

/// <summary>
/// Pins <see cref="IExDefinitionContributor"/>, discovered by <see cref="EntityRegistry.RegisterAll"/>
/// and run by <see cref="ExDefinitionModSystem.AssetsLoaded"/> before injection.
/// </summary>
[Collection("ExDefinitions")]
public class DefinitionContributorTests : IDisposable {
  public DefinitionContributorTests() {
    ExDefinitions.Clear();
    ExDefinitions.Logger = null;
  }

  public void Dispose() {
    ExDefinitions.Clear();
    ExDefinitions.Logger = null;
    var field = typeof(EntityRegistry).GetField(
      "_domainByAssembly",
      BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var map = (Dictionary<Assembly, string>)field.GetValue(null)!;
    map.Remove(typeof(DefinitionContributorTests).Assembly);
  }

  private sealed class TestContributor : IExDefinitionContributor {
    public void Contribute(ICoreAPI api) =>
      ExDefinitions.RegisterItem(
        ExItemDef.Create("exlibtest.contrib", "contributed")
      );
  }

  private sealed class ThrowingContributor : IExDefinitionContributor {
    public void Contribute(ICoreAPI api) =>
      throw new InvalidOperationException("boom");
  }

  // A contributor whose one constructor takes an int, outside the test assembly that other classes
  // register whole.
  private static Type EmitNoCtorContributor() {
    var asm = AssemblyBuilder.DefineDynamicAssembly(
      new AssemblyName($"noctorcontributor.{Guid.NewGuid():N}"),
      AssemblyBuilderAccess.Run
    );
    TypeBuilder builder = asm.DefineDynamicModule("noctorcontributor")
      .DefineType(
        "noctorcontributor.NoCtorContributor",
        TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
        typeof(object),
        [typeof(IExDefinitionContributor)]
      );
    ConstructorBuilder ctor = builder.DefineConstructor(
      MethodAttributes.Public,
      CallingConventions.Standard,
      [typeof(int)]
    );
    ILGenerator il = ctor.GetILGenerator();
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
    il.Emit(OpCodes.Ret);
    MethodInfo contract = typeof(IExDefinitionContributor).GetMethod(
      nameof(IExDefinitionContributor.Contribute)
    )!;
    MethodBuilder contribute = builder.DefineMethod(
      contract.Name,
      MethodAttributes.Public
        | MethodAttributes.Virtual
        | MethodAttributes.Final
        | MethodAttributes.HideBySig
        | MethodAttributes.NewSlot,
      typeof(void),
      [typeof(ICoreAPI)]
    );
    contribute.GetILGenerator().Emit(OpCodes.Ret);
    builder.DefineMethodOverride(contribute, contract);
    return builder.CreateType();
  }

  // Every scan of this assembly runs ThrowingContributor with the rest.
  private static void ExpectThrowingContributor(TestWorld world) {
    world.Log.Expect(EnumLogType.Error, "ThrowingContributor threw");
    world.Log.Expect(EnumLogType.Error, "boom");
  }

  private static Mod FakeMod(string modId) {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = modId }
    );
    return mod;
  }

  [Fact]
  public void RegisterAll_discovers_contributors_in_the_scanned_assembly() {
    var world = new TestWorld();

    EntityRegistry.RegisterAll(
      world.Api,
      FakeMod("exlibtest.contrib"),
      typeof(TestContributor).Assembly
    );

    Assert.Contains(typeof(TestContributor), ExDefinitions.Contributors);
  }

  [Fact]
  public void The_definition_system_runs_contributors_before_injecting() {
    var world = new TestWorld();
    Assert.Equal(EnumAppSide.Server, world.Api.Side);
    EntityRegistry.RegisterAll(
      world.Api,
      FakeMod("exlibtest.contrib"),
      typeof(TestContributor).Assembly
    );

    ExpectThrowingContributor(world);
    new ExDefinitionModSystem().AssetsLoaded(world.Api);

    Assert.Contains(ExDefinitions.Items, d => d.Code == "contributed");
    world
      .Api.Assets.Received()
      .Add(
        Arg.Is<AssetLocation>(l => l.Path.Contains("contributed")),
        Arg.Any<IAsset>()
      );
  }

  [Fact]
  public void A_throwing_contributor_is_logged_and_the_rest_still_run() {
    var world = new TestWorld();
    // One scan discovers both contributors in this assembly.
    ExDefinitions.DiscoverContributors(typeof(ThrowingContributor).Assembly);
    ExpectThrowingContributor(world);

    ExDefinitions.RunContributors(world.Api);

    Assert.Contains(world.Log.Errors, e => e.Contains("ThrowingContributor"));
    Assert.Contains(ExDefinitions.Items, d => d.Code == "contributed");
  }

  [Fact]
  public void A_contributor_without_a_parameterless_constructor_is_skipped_with_a_warning() {
    var logger = new RecordingLogger();
    logger.Expect(EnumLogType.Warning, "NoCtorContributor");
    ExDefinitions.Logger = logger;
    Type noCtor = EmitNoCtorContributor();

    ExDefinitions.DiscoverContributors(noCtor.Assembly);

    Assert.Contains(logger.Warnings, w => w.Contains("NoCtorContributor"));
    Assert.DoesNotContain(noCtor, ExDefinitions.Contributors);
  }
}
