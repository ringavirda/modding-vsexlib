#if GAME_GE_1_22
using System;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="StructureBreaks"/> against small megablocks and constructions that each break
/// one way: whole, leaving fillers, throwing, dropping nothing, or refunding through a wildcard the
/// construction never stored.</summary>
public class StructureBreaksTests
{
  private static readonly FillerCellSpec[] TwoCells =
  [
    new(-1, 0, 0),
    new(1, 0, 0),
  ];

  private static ExBlockDef Mega(
    string code,
    string cls = "ExFilledMegastructure"
  ) => ExBlockDef.Create("test", code).Class(cls).FillerOffsets(TwoCells);

  private static StructureBreaks.Result Run(params ExBlockDef[] defs) =>
    StructureBreaks.Run(
      defs,
      [typeof(BlockStructureFiller).Assembly],
      world =>
      {
        world.RegisterClass("test-leavesfillers", typeof(LeavesFillers));
        world.RegisterClass("test-throwsonbreak", typeof(ThrowsOnBreak));
        world.RegisterClass("test-dropsnothing", typeof(DropsNothing));
        world.RegisterClass("test-plain", typeof(PlainBe));
      }
    );

  // Fails when a fresh TestWorld runs no break or no removal hooks: the fillers outlive the
  // principal.
  [Fact]
  public void A_megablock_that_clears_its_footprint_passes_from_every_cell()
  {
    StructureBreaks.Result result = Run(
      Mega("mega").VariantGroup("side", "n", "e")
    );

    Assert.Empty(result.Failures);
    Assert.Equal(1, result.Blocks);
    Assert.Equal(2, result.Variants);
    Assert.Equal(2 * (1 + TwoCells.Length), result.Breaks);
  }

  // Fails when the standing-cell check is dropped.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_megablock_that_leaves_its_fillers_fails_as_left_standing()
  {
    StructureBreaks.Result result = Run(Mega("leaves", "test-leavesfillers"));

    Assert.Equal(1 + TwoCells.Length, result.Failures.Count);
    Assert.All(
      result.Failures,
      f => Assert.Contains("left", f, StringComparison.Ordinal)
    );
    Assert.Contains("broken from the principal", result.Failures[0]);
  }

  // Fails when the break is not wrapped in the catch that records it.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_break_that_throws_fails_with_the_exception()
  {
    StructureBreaks.Result result = Run(Mega("throws", "test-throwsonbreak"));

    Assert.Equal(1 + TwoCells.Length, result.Failures.Count);
    Assert.All(
      result.Failures,
      f => Assert.Contains("threw InvalidOperationException", f)
    );
  }

  // Fails when AddDefinitionDrops adds nothing to the expected drops.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_break_that_drops_nothing_fails_against_the_definition_drops()
  {
    StructureBreaks.Result result = Run(Mega("empty", "test-dropsnothing"));

    Assert.Equal(1 + TwoCells.Length, result.Failures.Count);
    Assert.All(
      result.Failures,
      f => Assert.EndsWith("dropped test:empty x0 (definition: 1..1)", f)
    );
  }

  // Fails when Build stops seeding StoredWildCards (the refund throws KeyNotFoundException), or when
  // a part-built stage is broken from the principal alone.
  [Fact]
  public void A_built_construction_refunds_every_paid_stage_from_every_cell()
  {
    StructureBreaks.Result result = Run(
      Mega("built")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.RequireMetalPlate("test", 2))
            .Stage(s => s.Require("game:plank-oak", 3))
        )
    );

    Assert.Empty(result.Failures);
    Assert.Equal(3 * (1 + TwoCells.Length), result.Breaks);
  }

  // Fails when Build takes the salvage ratio as 1: a half refund of 3 is 1..2, never 3.
  [Fact]
  public void A_construction_refunds_at_its_salvage_ratio()
  {
    StructureBreaks.Result result = Run(
      Mega("salvage")
        .EntityClass("test-plain")
        .Construction(c =>
          c.BrokenDropsRatio(0.5f)
            .Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.Require("game:plank-oak", 3))
        )
    );

    Assert.Empty(result.Failures);
  }

  // Fails when GetConstructionDrops stops refunding the stage just completed: the wildcard stage is
  // then never resolved.
  [Fact]
  public void A_wildcard_ingredient_with_no_stored_wildcard_throws_on_break()
  {
    StructureBreaks.Result result = Run(
      Mega("wild")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.Require("game:plank-*", 2))
        )
    );

    Assert.NotEmpty(result.Failures);
    Assert.All(
      result.Failures,
      f => Assert.Contains("threw NullReferenceException", f)
    );
    Assert.Contains(result.Failures, f => f.Contains("at stage 1"));
  }

  // Fails when StageCount returns 0 for a definition whose stages did not stand up.
  [Fact]
  public void Stages_without_an_entity_class_fail_as_not_stood_up()
  {
    StructureBreaks.Result result = Run(
      Mega("noentity")
        .Construction(c => c.Stage(s => s.Require("game:stick", 1)))
    );

    Assert.Equal(0, result.Breaks);
    string failure = Assert.Single(result.Failures);
    Assert.Contains("stands up without them", failure);
  }

  // Fails when Build seeds a stored wildcard with null instead of refusing, or when a stage that
  // cannot be stood up is retried from every cell.
  [Fact]
  public void A_stored_wildcard_with_no_allowed_variant_cannot_be_stood_up()
  {
    StructureBreaks.Result result = Run(
      Mega("novariant")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.Require("game:plank-*", 2, storeWildCard: "wood"))
        )
    );

    string failure = Assert.Single(
      result.Failures,
      f => f.Contains("at stage 1", StringComparison.Ordinal)
    );
    Assert.Contains("at stage 1 could not be stood up", failure);
    Assert.Contains("names no allowed variant", failure);
  }

  // Fails when InScope reads only the plain attributes.
  [Fact]
  public void InScope_reads_filler_offsets_by_type()
  {
    ExBlockDef byType = ExBlockDef
      .Create("test", "bytype")
      .VariantGroup("side", "n", "e")
      .FillerOffsetsByType("*-n", TwoCells);

    Assert.True(StructureBreaks.InScope(byType));
    Assert.False(StructureBreaks.InScope(ExBlockDef.Create("test", "plain")));
  }

  // Fails when Run stands up a definition InScope rejects.
  [Fact]
  public void A_definition_out_of_scope_is_not_stood_up()
  {
    StructureBreaks.Result result = Run(ExBlockDef.Create("test", "plain"));

    Assert.Equal(0, result.Blocks);
    Assert.Equal(0, result.Breaks);
  }

  private sealed class LeavesFillers : BlockFilledMegastructure
  {
    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) { }
  }

  private sealed class ThrowsOnBreak : BlockFilledMegastructure
  {
    public override void OnBlockBroken(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) => throw new InvalidOperationException("fixture");
  }

  private sealed class DropsNothing : BlockFilledMegastructure
  {
    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      float dropQuantityMultiplier = 1f
    ) => [];
  }

  private sealed class PlainBe : BlockEntity { }
}
#endif
