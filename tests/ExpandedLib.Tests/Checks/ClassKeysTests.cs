using ExpandedLib.Checks;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>How each shipped <see cref="ICheckSource"/> resolves a block class key and a
/// block-entity behaviour key.</summary>
public class ClassKeysTests {
  private const string FillerKey = "exlib.BlockStructureFiller";
  private const string MemberKey = TestWorld.NetworkMemberClass;

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
}
