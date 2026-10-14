#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The <c>break</c> step's pure parts: the tier check, who breaks, the drops a block declares
/// and how <c>expect drops</c> reads what dropped (scenarios.md, <c>break</c>).</summary>
public class BreakStepTests {
  private static readonly Item Ore = new() {
    Code = new AssetLocation("test:ore"),
    ItemId = 910,
  };
  private static readonly Item Flint = new() {
    Code = new AssetLocation("test:flint"),
    ItemId = 911,
  };

  private sealed class Dropper : Block {
    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) => [new ItemStack(Ore, 2)];
  }

  /// <summary>Fails when a tool below the block's tier is let through, or a tool that meets it, or
  /// a block that needs none, is refused.</summary>
  [Fact]
  public void A_held_tool_below_the_blocks_tier_is_refused() {
    Assert.NotNull(ScenarioPlayer.TierRefusal(2, 1));
    Assert.NotNull(ScenarioPlayer.TierRefusal(1, 0));
    Assert.Null(ScenarioPlayer.TierRefusal(2, 2));
    Assert.Null(ScenarioPlayer.TierRefusal(0, 0));
  }

  /// <summary>Fails when a break by the world is given the scenario player, or a break by the player
  /// is not.</summary>
  [Fact]
  public void A_break_by_the_world_has_no_breaker() {
    IPlayer player = new TestWorld().Player().Player;

    Assert.Null(BreakStep.Breaker("world", player));
    Assert.Same(player, BreakStep.Breaker("player", player));
  }

  /// <summary>Fails when the listed counts compare the first stack of a code instead of the sum of
  /// every stack of it.</summary>
  [Fact]
  public void Drops_sum_per_code() {
    ItemStack[] dropped = [new(Ore, 2), new(Flint, 1), new(Ore, 3)];

    Assert.Null(BreakStep.Mismatch([("test:ore", 5)], dropped, false));
    Assert.NotNull(BreakStep.Mismatch([("test:ore", 2)], dropped, false));
  }

  /// <summary>Fails when exact ignores a stack the list does not name, or refuses one without
  /// exact.</summary>
  [Fact]
  public void Exact_drops_refuse_an_unlisted_stack() {
    ItemStack[] dropped = [new(Ore, 2), new(Flint, 1)];

    Assert.Null(BreakStep.Mismatch([("test:ore", 2)], dropped, false));
    string? mismatch = BreakStep.Mismatch([("test:ore", 2)], dropped, true);
    Assert.Contains("test:flint x1, not listed", mismatch);
  }

  /// <summary>Fails when a listed code that dropped nothing passes.</summary>
  [Fact]
  public void A_listed_code_that_did_not_drop_fails() {
    string? mismatch = BreakStep.Mismatch([("test:ore", 1)], [], false);

    Assert.Contains("test:ore x0, expected x1", mismatch);
  }

  /// <summary>Fails when declared drops allow a count more than 1 off, a code that dropped without
  /// being declared, or a declared code that did not drop.</summary>
  [Fact]
  public void Declared_drops_allow_a_rounding_of_one_and_the_same_codes() {
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
  }

  /// <summary>Fails when the declared drops of a plain block are not its own
  /// <c>Block.GetDrops</c>.</summary>
  [Fact]
  public void A_block_declares_its_own_drops() {
    var world = new TestWorld();
    var at = new BlockPos(2, 2, 2);
    world.Place(at, TestBlocks.Configure(new Dropper(), "test:dropper", 80));

    List<ItemStack> declared = BreakStep.DeclaredDrops(world.World, at, null);

    Assert.Equal(2, BreakStep.Sum(declared)["test:ore"]);
  }

  /// <summary>Fails when a filler cell declares its own (empty) drops instead of its principal's.</summary>
  [Fact]
  public void A_filler_declares_its_principals_drops() {
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
}
#endif
