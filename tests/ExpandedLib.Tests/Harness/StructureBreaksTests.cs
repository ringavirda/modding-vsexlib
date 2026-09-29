#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="StructureBreaks"/> against small megablocks and constructions that each break
/// one way: whole, leaving fillers, throwing, dropping nothing, refunding through a wildcard the
/// construction never stored, or other than it was paid; code-first, and loaded from the JSON of the
/// BreakFixture mod.</summary>
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
    Run(_ => { }, defs);

  private static StructureBreaks.Result Run(
    Action<TestWorld> prepare,
    params ExBlockDef[] defs
  ) =>
    StructureBreaks.Run(
      defs,
      [typeof(BlockStructureFiller).Assembly],
      world =>
      {
        world.RegisterClass("test-leavesfillers", typeof(LeavesFillers));
        world.RegisterClass("test-throwsonbreak", typeof(ThrowsOnBreak));
        world.RegisterClass("test-dropsnothing", typeof(DropsNothing));
        world.RegisterClass("test-plain", typeof(PlainBe));
        world.RegisterClass("test-addsstick", typeof(AddsAStick));
        world.RegisterClass("test-onlyflint", typeof(OnlyAFlint));
        prepare(world);
      }
    );

  /// <summary>Breaks per cell of a three-stage construction: stage 0 once, stages 1 and 2 once per
  /// payment.</summary>
  private const int ThreeStageBreaks = 1 + 2 * 3;

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

  // Fails when AddOwnDrops adds nothing to the expected drops.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_break_that_drops_nothing_fails_against_the_definition_drops()
  {
    StructureBreaks.Result result = Run(Mega("empty", "test-dropsnothing"));

    Assert.Equal(1 + TwoCells.Length, result.Failures.Count);
    Assert.All(
      result.Failures,
      f => Assert.EndsWith("dropped test:empty x0 (expected: 1..1)", f)
    );
  }

  // Fails when AddOwnDrops leaves out a behaviour's stacks that do not replace the definition drops.
  [Fact]
  public void A_behaviour_adding_to_the_drops_is_expected_beside_them()
  {
    StructureBreaks.Result result = Run(
      world => world.RegisterItem("game:stick"),
      Mega("adds").Behavior("test-addsstick")
    );

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.All(
      result.Spawned,
      s =>
        Assert.Equal(
          ["game:stick x1", "test:adds x1"],
          s.Stacks.Select(t => $"{t.Collectible.Code} x{t.StackSize}")
            .Order(StringComparer.Ordinal)
        )
    );
  }

  // Fails when AddOwnDrops reads the behaviours of a block whose class overrides GetDrops: the stick
  // the class never drops is then expected.
  [Fact]
  public void A_class_overriding_its_drops_is_held_to_its_definition_drops()
  {
    StructureBreaks.Result result = Run(
      world => world.RegisterItem("game:stick"),
      Mega("overrides", "test-dropsnothing")
        .NoDrops()
        .Behavior("test-addsstick")
    );

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.Equal(1 + TwoCells.Length, result.Breaks);
    Assert.All(result.Spawned, s => Assert.Empty(s.Stacks));
  }

  // Fails when AddOwnDrops reads a behaviour that prevents what follows as one that passes through:
  // the earlier behaviour's stick and the block itself are then expected.
  [Fact]
  public void A_behaviour_preventing_what_follows_leaves_only_its_own_drops()
  {
    StructureBreaks.Result result = Run(
      world =>
      {
        world.RegisterItem("game:stick");
        world.RegisterItem("game:flint");
      },
      Mega("only").Behavior("test-addsstick").Behavior("test-onlyflint")
    );

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.Equal(1 + TwoCells.Length, result.Breaks);
    Assert.All(
      result.Spawned,
      s =>
        Assert.Equal(
          "game:flint x1",
          Assert.Single(s.Stacks) is var t
            ? $"{t.Collectible.Code} x{t.StackSize}"
            : null
        )
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
    Assert.Equal(ThreeStageBreaks * (1 + TwoCells.Length), result.Breaks);
  }

  // Fails when Build expects a later stage's {metal} unfilled: the refund names the stored metal.
  [Fact]
  public void A_later_stage_taking_the_stored_metal_refunds_that_metal()
  {
    StructureBreaks.Result result = Run(
      Mega("taken")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.RequireMetalPlate("test", 2))
            .Stage(s => s.RequireMetalRod("test", 1))
        )
    );

    Assert.Empty(result.Failures);
    Assert.Equal(ThreeStageBreaks * (1 + TwoCells.Length), result.Breaks);
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
            .Stage(s => s.Require("game:plank-*", 2, allowedVariants: ["oak"]))
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

    var failures = result
      .Failures.Where(f => f.Contains("at stage 1", StringComparison.Ordinal))
      .ToList();
    Assert.Equal(3, failures.Count);
    Assert.All(
      failures,
      f =>
      {
        Assert.Contains("could not be stood up", f);
        Assert.Contains(
          "stage 1 stores wildcard 'wood' for game:plank-* but names no allowed variant, and the world holds none",
          f
        );
      }
    );
  }

  // Fails when Offers pays a wildcard without allowed variants in nothing the world holds (the
  // stage cannot be stood up), or in a variant the ingredient skips (the game refuses birch).
  [Fact]
  public void A_stored_wildcard_with_no_allowed_variant_is_paid_in_one_the_world_holds()
  {
    StructureBreaks.Result result = Run(
      world =>
      {
        int id = 61000;
        foreach (string wood in new[] { "birch", "oak", "pine" })
          world.Register(
            TestBlocks.Configure(
              new Block(),
              $"game:plank-{wood}",
              id++,
              ("wood", wood)
            )
          );
      },
      Mega("anywood")
        .EntityClass("test-plain")
        .EntityBehavior(
          "ExRightClickConstructable",
          JObject.Parse(
            """
            {
              "stages": [
                { "requireStacks": [{ "type": "item", "code": "game:stick", "quantity": 1 }] },
                {
                  "requireStacks": [
                    {
                      "type": "block",
                      "code": "game:plank-*",
                      "quantity": 2,
                      "storeWildCard": "wood",
                      "skipVariants": ["birch"]
                    }
                  ]
                }
              ]
            }
            """
          )
        )
    );

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.Equal((1 + 3) * (1 + TwoCells.Length), result.Breaks);
    Assert.Equal(
      "game:plank-oak x2, game:stick x1",
      string.Join(
        ", ",
        result
          .Spawned.Single(s =>
            s.Stage == 1
            && s.Cell == -1
            && s.Paid == StructureBreaks.Payment.Survival
          )
          .Stacks.Select(s => $"{s.Collectible.Code} x{s.StackSize}")
          .Where(s => !s.StartsWith("test:", StringComparison.Ordinal))
          .Order(StringComparer.Ordinal)
      )
    );
  }

  // Fails when InScope reads only the plain attributes.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.InScope))]
  public void InScope_reads_filler_offsets_by_type()
  {
    ExBlockDef byType = ExBlockDef
      .Create("test", "bytype")
      .VariantGroup("side", "n", "e")
      .FillerOffsetsByType("*-n", TwoCells);

    Assert.True(StructureBreaks.InScope(byType));
  }

  // Fails when InScope stops reading construction stages.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.InScope))]
  public void InScope_reads_construction_stages()
  {
    ExBlockDef staged = ExBlockDef
      .Create("test", "staged")
      .Construction(c => c.Stage(s => s.AddElements("Root")));

    Assert.True(StructureBreaks.InScope(staged));
  }

  [Fact]
  public void A_definition_with_neither_is_out_of_scope()
  {
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

  #region Payments

  // Fails when the payment with Ctrl held is made without Ctrl: the gear is then paid and stored.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_stage_keyed_other_than_wood_or_metal_fails_the_break_paid_with_ctrl_held()
  {
    StructureBreaks.Result result = Run(
      Mega("gear")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s =>
              s.Require(
                "game:gear-*",
                1,
                storeWildCard: "gear",
                allowedVariants: ["rusty"]
              )
            )
        )
    );

    Assert.Equal(1 + TwoCells.Length, result.Failures.Count);
    Assert.All(
      result.Failures,
      f =>
      {
        Assert.Contains(
          "at stage 1 paid in creative with Ctrl held broken from",
          f
        );
        Assert.Contains(
          "threw KeyNotFoundException: The given key 'gear' was not present in the dictionary.",
          f
        );
      }
    );
  }

  // Fails when every stage is paid in the same allowed variant: the shared key then refunds what
  // each stage paid.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void Two_stages_storing_one_key_fail_the_refund_comparison()
  {
    string[] woods = ["oak", "birch"];
    StructureBreaks.Result result = Run(
      Mega("shared")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s =>
              s.Require(
                "game:plank-*",
                2,
                storeWildCard: "wood",
                allowedVariants: woods
              )
            )
            .Stage(s =>
              s.Require(
                "game:plank-*",
                3,
                storeWildCard: "wood",
                allowedVariants: woods
              )
            )
        )
    );

    Assert.Equal(2 * (1 + TwoCells.Length), result.Failures.Count);
    Assert.All(
      result.Failures,
      f =>
      {
        Assert.Contains("at stage 2 paid in ", f);
        Assert.DoesNotContain("with Ctrl held", f);
        Assert.EndsWith(
          "dropped game:plank-birch x5 (expected: 3..3), game:plank-oak x0 (expected: 2..2)",
          f
        );
      }
    );
  }

  // Fails when ExRightClickConstructable admits a stored key in two variants inside the stage that
  // stores it: the plate and rod paid in steel and iron are then taken.
  [Fact]
  public void A_stage_storing_its_metal_refuses_two_metals()
  {
    StructureBreaks.Result result = Run(
      Mega("onemetal")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s =>
              s.RequireMetalPlate("test", 2).RequireMetalRod("test", 1)
            )
            .Stage(s => s.RequireMetalNails("test", 1))
        )
    );

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.Equal(ThreeStageBreaks * (1 + TwoCells.Length), result.Breaks);
  }

  // Fails when the run offers no mixed payment before paying a stage that stores a key.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_constructable_taking_two_metals_in_one_stage_fails()
  {
    StructureBreaks.Result result = Run(
      world =>
        world.RegisterClass("ExRightClickConstructable", typeof(TakesAnyMetal)),
      Mega("anymetal")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s => s.RequireMetalPlate("test", 2))
            .Stage(s => s.RequireMetalRod("test", 1))
        )
    );

    Assert.Equal(2 * 2, result.Failures.Count);
    Assert.All(
      result.Failures,
      f =>
        Assert.Contains(
          "stage 1 took 'metal' in two variants, steel and iron",
          f
        )
    );
  }

  // Fails when the creative payment holds Ctrl: the game then records oak, not the birch paid.
  [Fact]
  public void Creative_without_ctrl_pays_from_the_hotbar_as_survival_does()
  {
    StructureBreaks.Result result = Run(
      Mega("birch")
        .EntityClass("test-plain")
        .Construction(c =>
          c.Stage(s => s.Require("game:stick", 1))
            .Stage(s =>
              s.Require(
                "game:plank-*",
                2,
                storeWildCard: "wood",
                allowedVariants: ["birch"]
              )
            )
        )
    );

    string Refund(StructureBreaks.Payment paid) =>
      string.Join(
        ", ",
        result
          .Spawned.Single(s => s.Stage == 1 && s.Cell == -1 && s.Paid == paid)
          .Stacks.Select(s => $"{s.Collectible.Code} x{s.StackSize}")
          .Where(s => !s.StartsWith("test:", StringComparison.Ordinal))
          .Order(StringComparer.Ordinal)
      );
    Assert.Empty(result.Failures);
    Assert.Equal(
      "game:plank-birch x2, game:stick x1",
      Refund(StructureBreaks.Payment.Survival)
    );
    Assert.Equal(
      "game:plank-birch x2, game:stick x1",
      Refund(StructureBreaks.Payment.Creative)
    );
    Assert.Equal(
      "game:plank-oak x2, game:stick x1",
      Refund(StructureBreaks.Payment.CreativeWithCtrl)
    );
  }

  #endregion

  private static TestWorld LoadFixture() =>
    new TestWorld().LoadAssets(
      Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Harness",
        "Fixtures",
        "BreakFixture"
      )
    );

  private static bool IsFrame(Block block) =>
    block.Code.Path.StartsWith("frame-");

  // Fails when AddOwnDrops reads only the definition drops, or adds them after a behaviour prevented
  // the default: vanilla's HorizontalOrientable drops the north variant of every facing.
  [Fact]
  public void A_block_dropping_its_behaviours_face_breaks_clean_from_every_cell()
  {
    using TestWorld world = LoadFixture();

    StructureBreaks.Result result = StructureBreaks.Run(
      world,
      b => b.Code.Path.StartsWith("turned-")
    );

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.Equal(2 * (1 + TwoCells.Length), result.Breaks);
    Assert.All(
      result.Spawned,
      s =>
        Assert.Equal(
          "breakfixture:turned-north x1",
          Assert.Single(s.Stacks) is var t
            ? $"{t.Collectible.Code} x{t.StackSize}"
            : null
        )
    );
  }

  // Fails when LoadAssets keeps the loader's classes from the world, when the run registers no
  // mechanical power system, or when the fixture's metal plate loses its storeWildCard.
  [Fact]
  public void A_json_megablock_breaks_clean_from_every_cell_at_every_stage()
  {
    using TestWorld world = LoadFixture();

    StructureBreaks.Result result = StructureBreaks.Run(world, IsFrame);

    Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures));
    Assert.Equal(1, result.Blocks);
    Assert.Equal(2, result.Variants);
    Assert.Equal(2 * ThreeStageBreaks * (1 + TwoCells.Length), result.Breaks);
    Assert.Single(world.Mods.Systems.OfType<MechanicalPowerMod>());
  }

  // Fails when a break's spawns are not recorded, or when the world's drops are not cleared before
  // each break: the complete frame's refund then carries the earlier breaks' stacks.
  [Fact]
  public void Each_break_reports_every_stack_it_spawned()
  {
    using TestWorld world = LoadFixture();

    StructureBreaks.Result result = StructureBreaks.Run(world, IsFrame);

    Assert.Equal(result.Breaks, result.Spawned.Count);
    StructureBreaks.Spawn complete = Assert.Single(
      result.Spawned,
      s =>
        s.Code == "breakfixture:frame-e"
        && s.Stage == 2
        && s.Cell == 1
        && s.Paid == StructureBreaks.Payment.Survival
    );
    Assert.Equal(
      ["breakfixture:frame-e x1", "game:metalplate-iron x2", "game:stick x1"],
      complete
        .Stacks.Select(s => $"{s.Collectible.Code} x{s.StackSize}")
        .Order(StringComparer.Ordinal)
    );
  }

  // Fails when the run skips the blocks no filter names (include null), or when a break that throws
  // is not recorded with its exception.
  [Fact]
  [PlantedDefect(typeof(StructureBreaks), nameof(StructureBreaks.Run))]
  public void A_json_wildcard_ingredient_without_storeWildCard_throws_on_break()
  {
    using TestWorld world = LoadFixture();

    StructureBreaks.Result result = StructureBreaks.Run(world);

    Assert.Equal(3, result.Blocks);
    Assert.Equal(6, result.Variants);
    Assert.Equal(2 * 3 * (1 + TwoCells.Length), result.Failures.Count);
    Assert.All(
      result.Failures,
      f =>
      {
        Assert.StartsWith("breakfixture:unstored-", f);
        Assert.Matches("at stage 2 paid in [a-zA-Z ]+ broken from", f);
        Assert.Contains("threw NullReferenceException", f);
      }
    );
  }

  // Fails when the run registers a mechanical power system over the one the world holds.
  [Fact]
  public void A_run_keeps_the_worlds_own_mechanical_power_system()
  {
    using TestWorld world = LoadFixture();
    var power = new MechanicalPowerMod();
    world.Mods.Register(power);
    power.Start(world.Api);

    StructureBreaks.Run(world, IsFrame);

    Assert.Same(
      power,
      Assert.Single(world.Mods.Systems.OfType<MechanicalPowerMod>())
    );
  }

  // Fails when each run places its first principal at the first site, over the leftovers of the
  // run before.
  [Fact]
  public void A_second_run_on_one_world_stands_clear_of_the_first()
  {
    using TestWorld world = LoadFixture();

    StructureBreaks.Result first = StructureBreaks.Run(
      world,
      b => b.Code.Path == "frame-n"
    );
    StructureBreaks.Result second = StructureBreaks.Run(
      world,
      b => b.Code.Path == "frame-e"
    );

    Assert.True(first.Failures.Count == 0, string.Join("\n", first.Failures));
    Assert.True(second.Failures.Count == 0, string.Join("\n", second.Failures));
    Assert.Equal(ThreeStageBreaks * (1 + TwoCells.Length), second.Breaks);
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

  private sealed class AddsAStick(Block block) : BlockBehavior(block)
  {
    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      ref float dropQuantityMultiplier,
      ref EnumHandling handling
    ) => [new ItemStack(world.GetItem(new AssetLocation("game:stick")))];
  }

  private sealed class OnlyAFlint(Block block) : BlockBehavior(block)
  {
    public override ItemStack[] GetDrops(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer byPlayer,
      ref float dropQuantityMultiplier,
      ref EnumHandling handling
    )
    {
      handling = EnumHandling.PreventSubsequent;
      return [new ItemStack(world.GetItem(new AssetLocation("game:flint")))];
    }
  }

  private sealed class TakesAnyMetal(BlockEntity be)
    : ExRightClickConstructable(be)
  {
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
      base.Initialize(api, properties);
      OnAttemptConstruct = null;
    }
  }
}
#endif
