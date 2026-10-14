#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The <c>break</c> step's pure parts: the tier check, who breaks, the drops a block declares
/// and how <c>expect drops</c> reads what dropped (scenarios.md, <c>break</c>).</summary>
public class BreakStepTests
{
  private static readonly Item Ore = new()
  {
    Code = new AssetLocation("test:ore"),
    ItemId = 910,
  };
  private static readonly Item Flint = new()
  {
    Code = new AssetLocation("test:flint"),
    ItemId = 911,
  };

  private sealed class Dropper : Block
  {
    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) => [new ItemStack(Ore, 2)];
  }

  private sealed class Recorder : Block
  {
    public int Broken;
    public IPlayer? By;
    public Action<BlockPos>? Spawn;

    public override void OnBlockBroken(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    )
    {
      Broken++;
      By = byPlayer;
      Spawn?.Invoke(pos);
    }
  }

  private static readonly ScenarioPlayer Unbuilt = (ScenarioPlayer)
    RuntimeHelpers.GetUninitializedObject(typeof(ScenarioPlayer));

  private static readonly BlockPos Cell = new(100, 50, 100);

  /// <summary>Fails when a tool below the block's tier is let through, or a tool that meets it, or
  /// a block that needs none, is refused.</summary>
  [Fact]
  public void A_held_tool_below_the_blocks_tier_is_refused()
  {
    Assert.NotNull(ScenarioPlayer.TierRefusal(2, 1));
    Assert.NotNull(ScenarioPlayer.TierRefusal(1, 0));
    Assert.Null(ScenarioPlayer.TierRefusal(2, 2));
    Assert.Null(ScenarioPlayer.TierRefusal(0, 0));
  }

  /// <summary>Fails when the gate reads the block's tier or the held tool's from anywhere but the block
  /// and the slot's stack: a tool of the tier passes, a lower one and an empty hand are refused, a block
  /// that needs none passes an empty hand.</summary>
  [Fact]
  public void The_tier_gate_reads_the_block_and_the_slots_tool()
  {
    var world = new TestWorld();
    Block hard = TestBlocks.Configure(new Block(), "test:hard", 82);
    hard.RequiredMiningTier = 2;
    Block soft = TestBlocks.Configure(new Block(), "test:soft", 83);
    var pick = new Item
    {
      Code = new AssetLocation("test:pick"),
      ItemId = 912,
      ToolTier = 2,
    };
    var stick = new Item
    {
      Code = new AssetLocation("test:stick"),
      ItemId = 913,
      ToolTier = 1,
    };

    string? Gate(Block block, Item? held) =>
      ScenarioPlayer.TierGate(
        block,
        world.World,
        Cell,
        new DummySlot(held == null ? null : new ItemStack(held))
      );

    Assert.Null(Gate(hard, pick));
    Assert.NotNull(Gate(hard, stick));
    Assert.NotNull(Gate(hard, null));
    Assert.Null(Gate(soft, null));
  }

  /// <summary>Fails when a break by the world is run as the scenario player, which a player given to the
  /// run would show as a break that is not the block's own with no breaker.</summary>
  [Fact]
  public void A_break_by_the_world_has_no_breaker()
  {
    var fixture = new LiveFixture();
    fixture.Context.Player = Unbuilt;
    var block = TestBlocks.Configure(new Recorder(), "test:recorder", 84);
    fixture.Test.Place(Cell, block);

    fixture.Run(
      LiveFixture
        .Case("""[{ "break": { "at": [0, 0, 0], "by": "world" } }]""")
        .Model.Steps
    );

    Assert.Equal(1, block.Broken);
    Assert.Null(block.By);
  }

  /// <summary>Fails when drops are collected only near the struck cell, so a drop spawned at the far end
  /// of the plot is missed, or when an item entity that was there before the break is counted.</summary>
  [Fact]
  public void Drops_are_the_items_the_break_spawned_within_the_plot()
  {
    var fixture = new LiveFixture();
    var block = TestBlocks.Configure(new Recorder(), "test:recorder", 84);
    fixture.Test.Place(Cell, block);
    var present = new List<(BlockPos At, EntityItem Item)>
    {
      (new BlockPos(105, 51, 100), Spawned(1, new ItemStack(Flint, 1))),
    };
    block.Spawn = _ =>
      present.Add(
        (new BlockPos(108, 51, 100), Spawned(2, new ItemStack(Ore, 3)))
      );
    fixture
      .Test.World.GetEntitiesInsideCuboid(
        Arg.Any<BlockPos>(),
        Arg.Any<BlockPos>(),
        Arg.Any<ActionConsumable<Entity>>()
      )
      .Returns(call =>
      {
        var min = call.ArgAt<BlockPos>(0);
        var max = call.ArgAt<BlockPos>(1);
        return present
          .Where(p =>
            p.At.X >= min.X
            && p.At.X <= max.X
            && p.At.Y >= min.Y
            && p.At.Y <= max.Y
            && p.At.Z >= min.Z
            && p.At.Z <= max.Z
          )
          .Select(p => (Entity)p.Item)
          .ToArray();
      });
    fixture.Context.PlotBounds = (
      new BlockPos(92, 50, 92),
      new BlockPos(116, 82, 116)
    );

    fixture.Run(
      LiveFixture
        .Case("""[{ "break": { "at": [0, 0, 0], "by": "world" } }]""")
        .Model.Steps
    );

    Assert.Equal(
      ["test:ore"],
      fixture.Context.LastBreak!.Dropped.Select(d =>
        d.Collectible.Code.ToString()
      )
    );
    Assert.Equal(3, fixture.Context.LastBreak.Dropped[0].StackSize);
  }

  private static EntityItem Spawned(long id, ItemStack stack) =>
    new() { EntityId = id, Itemstack = stack };

  /// <summary>Fails when the listed counts compare the first stack of a code instead of the sum of
  /// every stack of it.</summary>
  [Fact]
  public void Drops_sum_per_code()
  {
    ItemStack[] dropped = [new(Ore, 2), new(Flint, 1), new(Ore, 3)];

    Assert.Null(BreakStep.Mismatch([("test:ore", 5)], dropped, false));
    Assert.NotNull(BreakStep.Mismatch([("test:ore", 2)], dropped, false));
  }

  /// <summary>Fails when exact ignores a stack the list does not name, or refuses one without
  /// exact.</summary>
  [Fact]
  public void Exact_drops_refuse_an_unlisted_stack()
  {
    ItemStack[] dropped = [new(Ore, 2), new(Flint, 1)];

    Assert.Null(BreakStep.Mismatch([("test:ore", 2)], dropped, false));
    string? mismatch = BreakStep.Mismatch([("test:ore", 2)], dropped, true);
    Assert.Contains("test:flint x1, not listed", mismatch);
  }

  /// <summary>Fails when a listed code that dropped nothing passes.</summary>
  [Fact]
  public void A_listed_code_that_did_not_drop_fails()
  {
    string? mismatch = BreakStep.Mismatch([("test:ore", 1)], [], false);

    Assert.Contains("test:ore x0, expected x1", mismatch);
  }

  /// <summary>Fails when declared drops allow a count more than 1 off, a code that dropped without
  /// being declared, or a declared code that did not drop; or when a code declared in two stacks is
  /// held to one rounding instead of two.</summary>
  [Fact]
  public void Declared_drops_allow_a_rounding_of_one_and_the_same_codes()
  {
    ItemStack[] declared = [new(Ore, 3)];

    Assert.Null(BreakStep.DeclaredMismatch(declared, [new ItemStack(Ore, 4)]));
    Assert.Null(BreakStep.DeclaredMismatch(declared, [new ItemStack(Ore, 2)]));
    Assert.NotNull(
      BreakStep.DeclaredMismatch(declared, [new ItemStack(Ore, 5)])
    );
    Assert.NotNull(
      BreakStep.DeclaredMismatch(
        declared,
        [new ItemStack(Ore, 3), new ItemStack(Flint, 1)]
      )
    );
    Assert.NotNull(BreakStep.DeclaredMismatch(declared, []));

    ItemStack[] twice = [new(Ore, 2), new(Ore, 1)];
    Assert.Null(BreakStep.DeclaredMismatch(twice, [new ItemStack(Ore, 5)]));
    Assert.Null(BreakStep.DeclaredMismatch(twice, [new ItemStack(Ore, 1)]));
    Assert.NotNull(BreakStep.DeclaredMismatch(twice, [new ItemStack(Ore, 6)]));
  }

  /// <summary>Fails when the declared drops of a plain block are not its own
  /// <c>Block.GetDrops</c>.</summary>
  [Fact]
  public void A_block_declares_its_own_drops()
  {
    var world = new TestWorld();
    var at = new BlockPos(2, 2, 2);
    world.Place(at, TestBlocks.Configure(new Dropper(), "test:dropper", 80));

    List<ItemStack> declared = BreakStep.DeclaredDrops(world.World, at, null);

    Assert.Equal(2, BreakStep.Sum(declared)["test:ore"]);
  }

  /// <summary>Fails when a filler cell declares its own (empty) drops instead of its principal's.</summary>
  [Fact]
  public void A_filler_declares_its_principals_drops()
  {
    var world = new TestWorld();
    var principal = new BlockPos(4, 4, 4);
    var cell = new BlockPos(5, 4, 4);
    world.Place(
      principal,
      TestBlocks.Configure(new Dropper(), "test:dropper", 80)
    );
    world.Place(
      cell,
      TestBlocks.Configure(
        new BlockStructureFiller(),
        "exlib:structurefiller",
        81
      ),
      new BlockEntityStructureFiller { Principal = principal.Copy() }
    );

    List<ItemStack> declared = BreakStep.DeclaredDrops(world.World, cell, null);

    Assert.Equal(2, BreakStep.Sum(declared)["test:ore"]);
  }

  private sealed class StubBlockEntity : BlockEntity;

  /// <summary>A world holding a construction at <see cref="Cell"/> with two sticks paid in its
  /// second stage and that stage completed, so its refund is two sticks at ratio 1.</summary>
  private static TestWorld PaidConstruction()
  {
    var world = new TestWorld();
    world.RegisterItem("game:stick");
    var block = TestBlocks.Configure(new Block(), "test:frame", 85);
    var entity = new StubBlockEntity { Block = block, Pos = Cell.Copy() };
    world.Place(Cell, block, entity);
    entity.Initialize(world.Api);
    var construction = new ExRightClickConstructable(entity);
    construction.Initialize(
      world.Api,
      new JsonObject(
        new ConstructionStages()
          .Stage(_ => { })
          .Stage(s => s.Require("game:stick", 2))
          .Build()
      )
    );
    entity.Behaviors.Add(construction);
    ReflectionHelpers.SetField(
      ReflectionHelpers.GetField(construction, "rcc")!,
      "CurrentCompletedStage",
      1
    );
    return world;
  }

  /// <summary>Fails when the construction refund is left out for a survival breaker or the world, or
  /// is declared for a Creative one, for which <c>ExRightClickConstructable.OnBlockBroken</c> scatters
  /// nothing.</summary>
  [Fact]
  public void A_construction_declares_its_refund_except_to_a_creative_breaker()
  {
    TestWorld world = PaidConstruction();
    TestPlayer survival = world.Player("survivor");
    survival.GameMode = EnumGameMode.Survival;
    TestPlayer creative = world.Player("creator");
    creative.GameMode = EnumGameMode.Creative;

    Assert.Equal(
      2,
      BreakStep.Sum(
        BreakStep.DeclaredDrops(world.World, Cell, survival.Player)
      )["game:stick"]
    );
    Assert.Equal(
      2,
      BreakStep.Sum(BreakStep.DeclaredDrops(world.World, Cell, null))[
        "game:stick"
      ]
    );
    Assert.Empty(BreakStep.DeclaredDrops(world.World, Cell, creative.Player));
  }
}
#endif
