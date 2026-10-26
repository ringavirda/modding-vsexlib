using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every block entity type the library registers loads from a tree with none of its keys and
/// initialises on both sides, and a persisted member then holds its initializer.</summary>
public class EmptyTreeSweepTests {
  // The least a type needs to initialise, by type; a pipe joins its network on Initialize.
  private static readonly Dictionary<
    Type,
    Action<TestWorld, BlockEntity>
  > Setup = new() {
    [typeof(BlockEntityPipe)] = RegisterPipeNetwork,
    [typeof(BlockEntityPipePassthrough)] = RegisterPipeNetwork,
  };

  private static void RegisterPipeNetwork(TestWorld world, BlockEntity _) =>
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

  private sealed class NullLabelInitializer : ExBlockEntity {
    [Persist]
    private string? _label;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      _ = _label!.Length;
    }
  }

  private sealed class SeededLabelInitializer : ExBlockEntity {
    [Persist]
    private string? _label = "init";

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      _ = _label!.Length;
    }
  }

  private sealed class StaleMember : ExBlockEntity {
    [Persist]
    private int _count = 4;

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldForResolving
    ) {
      base.FromTreeAttributes(tree, worldForResolving);
      _count = 0;
    }
  }

  private sealed class AutoTypedNode : BlockEntityNetworkNode {
    public override string NetworkType { get; set; } = "autotyped";
  }

  public static IEnumerable<object[]> Types() =>
    EmptyTreeSweep
      .BlockEntityTypes(
        typeof(ExpandedLibModSystem).Assembly,
        typeof(BlockEntityPipe).Assembly
      )
      .OrderBy(t => t.FullName, StringComparer.Ordinal)
      .Select(t => new object[] { t });

  [Theory]
  [MemberData(nameof(Types))]
  public void A_registered_type_loads_and_initialises_from_an_empty_tree(
    Type type
  ) {
    IReadOnlyList<string> problems = EmptyTreeSweep.Run(
      type,
      Setup.GetValueOrDefault(type)
    );

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void A_node_loaded_from_an_empty_tree_keeps_its_declared_network_type() {
    var world = new TestWorld();
    var node = new AutoTypedNode();
    world.Place(
      new BlockPos(0, 64, 0),
      TestBlocks.Configure(new Block(), "sweep:autotyped", 39001),
      node
    );

    node.FromTreeAttributes(new TreeAttribute(), world.World);

    Assert.Equal("autotyped", node.NetworkType);
  }

  [Fact]
  public void The_sweep_finds_the_registered_types() {
    IReadOnlyList<Type> types = EmptyTreeSweep.BlockEntityTypes(
      typeof(ExpandedLibModSystem).Assembly,
      typeof(BlockEntityPipe).Assembly
    );

    Assert.Contains(typeof(BlockEntityMultiblock), types);
    Assert.Contains(typeof(BlockEntityStructureFiller), types);
  }

  [Fact]
  public void A_fixture_whose_initializer_reads_a_null_string_is_reported() {
    IReadOnlyList<string> problems = EmptyTreeSweep.Run(
      typeof(NullLabelInitializer)
    );

    Assert.Contains(problems, p => p.StartsWith("server initialize:"));
    Assert.Contains(problems, p => p.StartsWith("client initialize:"));
  }

  [Fact]
  public void A_member_with_an_initializer_passes_the_same_initializer() {
    Assert.Empty(EmptyTreeSweep.Run(typeof(SeededLabelInitializer)));
  }

  [Fact]
  public void A_member_that_a_load_leaves_off_its_initializer_is_reported() {
    IReadOnlyList<string> problems = EmptyTreeSweep.Run(typeof(StaleMember));

    Assert.Contains(
      problems,
      p => p.StartsWith("server load:") && p.Contains("_count")
    );
    Assert.Contains(
      problems,
      p => p.StartsWith("client load:") && p.Contains("_count")
    );
  }
}
