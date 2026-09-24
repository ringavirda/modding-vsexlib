using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>How each shipped <see cref="ICheckSource"/> resolves a block class key and a
/// block-entity behaviour key.</summary>
public class ClassKeysTests {
  public ClassKeysTests() => TestModDomain.Register();

  private const string FillerKey = "exlib.BlockStructureFiller";
  private const string MemberKey = TestWorld.NetworkMemberClass;
  private static readonly Assembly Here = typeof(ClassKeysTests).Assembly;

  private sealed class PlantedKeyBlock : Block;

  [BlockEntityBehaviorRegister]
  private sealed class KeyedMember(BlockEntity be) : BlockEntityBehavior(be);

  private sealed class LooseKeyMember(BlockEntity be) : BlockEntityBehavior(be);

  // Fails when AssetCheckSource stops asking the game's class registry.
  [Fact]
  public void The_asset_source_answers_from_the_class_registry() {
    using var world = new TestWorld();
    world
      .Api.ClassRegistry.GetBlockClass(FillerKey)
      .Returns(typeof(BlockStructureFiller));
    world
      .Api.ClassRegistry.GetBlockEntityBehaviorClass(MemberKey)
      .Returns(typeof(BEBehaviorNetworkMember));
    ICheckSource source = new AssetCheckSource(world.Api);

    Assert.Equal(typeof(BlockStructureFiller), source.BlockClass(FillerKey));
    Assert.Equal(
      typeof(BEBehaviorNetworkMember),
      source.BlockEntityBehaviorClass(MemberKey)
    );
  }

  // Fails when RepoCheckSource stops resolving by reflection over its domains' assemblies.
  [Fact]
  public void The_repository_source_answers_by_reflection() {
    string? root = DefinitionGoldens.RepoRootOverride;
    try {
      ICheckSource source = new RepoCheckSource(RepoPaths.Root, "exlib");

      Assert.Equal(typeof(BlockStructureFiller), source.BlockClass(FillerKey));
      Assert.Equal(
        typeof(BEBehaviorNetworkMember),
        source.BlockEntityBehaviorClass(MemberKey)
      );
    } finally {
      DefinitionGoldens.RepoRootOverride = root;
    }
  }

  // Fails when the reflective index stops scanning the ExDomain assemblies a source references.
  [Fact]
  public void A_class_in_a_referenced_family_assembly_resolves() {
    ICheckSource source = new AssemblyCheckSource(
      ("plantedclasskeys", typeof(ClassKeysTests).Assembly)
    );

    Assert.Equal(
      typeof(BlockPipe),
      source.BlockClass(
        EntityRegistry.KeyFor("plantedclasskeys", typeof(BlockPipe))
      )
    );
  }

  // Fails when the reflective index answers a key no scanned type registers under.
  [Fact]
  public void An_unknown_key_resolves_to_null() {
    ICheckSource source = new AssemblyCheckSource(
      ("exlib", typeof(BlockStructureFiller).Assembly)
    );

    Assert.Null(source.BlockClass("exlib.NoSuchBlock"));
    Assert.Null(source.BlockEntityBehaviorClass("exlib.NoSuchBehavior"));
  }

  // Fails when the index keys a type under a domain its assembly is not given under, which also
  // logs the no-ExDomain warning once more per type for each further domain.
  [Fact]
  public void A_type_is_keyed_under_its_own_assembly_domain_only() {
    var registered =
      (Dictionary<Assembly, string>)
        typeof(EntityRegistry)
          .GetField(
            "_domainByAssembly",
            BindingFlags.NonPublic | BindingFlags.Static
          )!
          .GetValue(null)!;
    ILogger? was = EntityRegistry.Logger;
    var logger = new RecordingLogger();
    logger.Expect(EnumLogType.Warning, "declares no [assembly: ExDomain]");
    try {
      registered.Remove(Here);
      EntityRegistry.Logger = logger;
      ICheckSource source = new AssemblyCheckSource(
        ("planteda", Here),
        ("plantedb", typeof(BlockStructureFiller).Assembly)
      );

      Assert.Equal(
        typeof(PlantedKeyBlock),
        source.BlockClass("planteda.PlantedKeyBlock")
      );
      Assert.Null(source.BlockClass("plantedb.PlantedKeyBlock"));
      Assert.Equal(
        ReflectionScan
          .GetCandidateTypes(Here)
          .Count(typeof(Block).IsAssignableFrom),
        logger.Warnings.Count()
      );
    } finally {
      EntityRegistry.Logger = was;
      TestModDomain.Register();
    }
  }

  // Fails when the behaviour index keys a type by its bare code, so another domain's key of the
  // same code resolves to it, or stops resolving an unprefixed key by an unregistered class name.
  [Fact]
  public void A_behaviour_key_resolves_under_its_own_domain_only() {
    ICheckSource source = new AssemblyCheckSource(("plantedclasskeys", Here));

    Assert.Equal(
      typeof(KeyedMember),
      source.BlockEntityBehaviorClass($"{TestModDomain.Id}.KeyedMember")
    );
    Assert.Null(source.BlockEntityBehaviorClass("other.KeyedMember"));
    Assert.Null(source.BlockEntityBehaviorClass("KeyedMember"));
    Assert.Equal(
      typeof(LooseKeyMember),
      source.BlockEntityBehaviorClass("LooseKeyMember")
    );
  }
}
