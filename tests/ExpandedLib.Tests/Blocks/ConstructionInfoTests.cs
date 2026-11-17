using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Tests the stage progress lines of an unfinished construction's block info.
/// </summary>
public class ConstructionInfoTests {
  private sealed class StubBlockEntity : BlockEntity;

  private static TestWorld MetalWorld() {
    var world = new TestWorld();
    foreach (string metal in new[] { "iron", "steel" })
      foreach (string part in new[] { "metalplate", "rod" })
        world.RegisterItem($"game:{part}-{metal}").VariantStrict["metal"] = metal;
    world.RegisterItem("game:hammer-iron");
    world.RegisterItem("game:saw-iron");
    return world;
  }

  // Stage 1 takes two plates and a rod, each storing metal; stage 2 takes a rod of the stored
  // metal; stage 3 takes nothing.
  private static ExRightClickConstructable Construction(TestWorld world) {
    var be = new StubBlockEntity {
      Block = TestBlocks.Configure(new Block(), "stub:infowork", 5003),
      Pos = new BlockPos(0, 0, 0, 0),
    };
    be.Initialize(world.Api);
    var behavior = new ExRightClickConstructable(be);
    var properties = new Definitions.ConstructionStages()
      .Stage(_ => { })
      .Stage(s => s.RequireMetalPlate("stub", 2).RequireMetalRod("stub", 1))
      .Stage(s => s.RequireMetalRod("stub", 1))
      .Stage(s => s.AddElements("Frame"))
      .Build();
    behavior.Initialize(world.Api, new JsonObject(properties));
    return behavior;
  }

  private static ExRightClickConstruction Rcc(
    ExRightClickConstructable behavior
  ) => (ExRightClickConstruction)ReflectionHelpers.GetField(behavior, "rcc")!;

  private static void Load(
    ExRightClickConstruction rcc,
    int stage,
    string? metal = null,
    int stepsPaid = 0
  ) {
    var tree = new TreeAttribute();
    tree.SetInt("currentStage", stage);
    tree.SetInt("stepsPaid", stepsPaid);
    if (metal != null) {
      var wildcards = new TreeAttribute();
      wildcards["metal"] = new StringAttribute(metal);
      tree["wildcards"] = wildcards;
    }
    rcc.FromTreeAttributes(tree);
  }

  private static List<object[]> Calls(string key) =>
    Lang.AvailableLanguages["en"]!
      .ReceivedCalls()
      .Where(c => c.GetMethodInfo().Name == "Get")
      .Select(c => c.GetArguments())
      .Where(a => (string?)a[0] == key)
      .Select(a => (object[])a[1]!)
      .ToList();

  private static string Info(ExRightClickConstruction rcc, TestWorld world) {
    TestLang.Init();
    Lang.AvailableLanguages["en"]!.ClearReceivedCalls();
    var sb = new StringBuilder();
    rcc.AppendStageInfo(world.Api.World, sb);
    return sb.ToString();
  }

  // Fails when the total counts Stages.Length instead of the payable stages, Stages.Length - 1.
  [Fact]
  public void Info_says_the_stage_reached_of_the_payable_stages() {
    TestWorld world = MetalWorld();
    ExRightClickConstruction rcc = Rcc(Construction(world));
    Load(rcc, 1, "iron");

    string text = Info(rcc, world);

    Assert.Contains(ExlibLang.ConstructionInfoUnfinished, text);
    Assert.Equal(
      new object[] { 1, 3 },
      Assert.Single(Calls(ExlibLang.ConstructionInfoUnfinished))
    );
  }

  // Fails when the IsComplete guard is dropped: a finished construction says it is unfinished.
  [Fact]
  public void A_complete_construction_appends_nothing() {
    TestWorld world = MetalWorld();
    ExRightClickConstruction rcc = Rcc(Construction(world));
    Load(rcc, 3, "iron");

    Assert.Equal("", Info(rcc, world));
  }

  // Fails when the stored wildcards are not filled into the ingredient, so the stage names the
  // first metal found instead of the chosen one.
  [Fact]
  public void Stack_lines_carry_the_count_and_the_stored_metal() {
    TestWorld world = MetalWorld();
    ExRightClickConstruction rcc = Rcc(Construction(world));
    Load(rcc, 1, "steel");

    Info(rcc, world);

    object[] line = Assert.Single(Calls(ExlibLang.ConstructionInfoStack));
    Assert.Equal(1, line[0]);
    Assert.Contains("rod-steel", (string)line[1]);
  }

  // Fails when the tool lines start at the first tool step instead of at StepsPaid.
  [Fact]
  public void Tool_lines_start_at_the_steps_paid() {
    TestWorld world = MetalWorld();
    ExRightClickConstruction rcc = Rcc(Construction(world));
    rcc.Stages = new[]
    {
      new ExConstructionStage(),
      new ExConstructionStage
      {
        Tool = "game:hammer-*",
        Steps = new[] { new ExConstructionToolStep { Tool = "game:saw-*" } },
      },
      new ExConstructionStage(),
    };
    Load(rcc, 0, null, 1);

    Info(rcc, world);

    object[] line = Assert.Single(Calls(ExlibLang.ConstructionInfoTool));
    Assert.Equal("exlib:construction-toolkind-saw", line[0]);
  }

  private static string ToolLineArgument(string pattern) {
    TestWorld world = MetalWorld();
    world.RegisterItem("iiex:trowel-iron");
    ExRightClickConstruction rcc = Rcc(Construction(world));
    rcc.Stages = new[]
    {
      new ExConstructionStage(),
      new ExConstructionStage
      {
        Steps = new[] { new ExConstructionToolStep { Tool = pattern } },
      },
      new ExConstructionStage(),
    };
    Load(rcc, 0);

    Info(rcc, world);

    return (string)Assert.Single(Calls(ExlibLang.ConstructionInfoTool))[0];
  }

  // Fails when the tool line is asked for the tool pattern instead of the kind name.
  [Fact]
  public void The_tool_line_asks_for_the_kind_key_of_the_pattern() {
    Assert.Equal(
      "exlib:construction-toolkind-hammer",
      ToolLineArgument("game:hammer-*")
    );
  }

  // Fails when a kind of a non-game domain is looked up in exlib instead of its own domain.
  [Fact]
  public void A_non_game_domain_asks_for_its_own_kind_key() {
    Assert.Equal(
      "iiex:construction-toolkind-trowel",
      ToolLineArgument("iiex:trowel-*")
    );
  }

  // Fails when the HasTranslation check is dropped: an unnamed kind shows its key, not a tool name.
  [Fact]
  public void A_kind_without_a_translation_falls_back_to_the_first_tool_name() {
    TestLang.Init();
    string key = "exlib:construction-toolkind-saw";
    TestLang.Service.HasTranslation(key, Arg.Any<bool>()).Returns(false);
    TestLang
      .Service.HasTranslation(key, Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(false);
    try {
      Assert.Contains("saw-iron", ToolLineArgument("game:saw-*"));
    } finally {
      TestLang.Service.HasTranslation(key, Arg.Any<bool>()).Returns(true);
      TestLang
        .Service.HasTranslation(key, Arg.Any<bool>(), Arg.Any<bool>())
        .Returns(true);
    }
  }

  // Fails when the stage info resolves the ingredient through the vanilla Resolve, which warns
  // that an exact code the world lacks failed to resolve.
  [Fact]
  public void An_exact_ingredient_absent_from_the_world_shows_its_code_and_logs_nothing() {
    TestWorld world = MetalWorld();
    ExRightClickConstruction rcc = Rcc(Construction(world));
    rcc.Stages = new[]
    {
      new ExConstructionStage(),
      new ExConstructionStage
      {
        RequireStacks = new[]
        {
          new ExConstructionIngredient
          {
            Code = new AssetLocation("game:mortar"),
            Type = EnumItemClass.Block,
            Quantity = 2,
          },
        },
      },
      new ExConstructionStage(),
    };
    Load(rcc, 0);

    Info(rcc, world);

    object[] line = Assert.Single(Calls(ExlibLang.ConstructionInfoStack));
    Assert.Equal(2, line[0]);
    Assert.Equal("game:mortar", line[1]);
    Assert.Empty(world.Log.Warnings);
  }

  // Fails when the creative-and-debug gate is restored on ExRightClickConstructable.GetBlockInfo.
  [Fact]
  public void A_survival_player_without_debug_mode_sees_the_lines() {
    TestWorld world = MetalWorld();
    ExRightClickConstructable behavior = Construction(world);
    TestPlayer player = world.Player();
    player.GameMode = EnumGameMode.Survival;
    TestLang.Init();
    var sb = new StringBuilder();

    behavior.GetBlockInfo(player.Player, sb);

    Assert.Contains(ExlibLang.ConstructionInfoUnfinished, sb.ToString());
  }
}
