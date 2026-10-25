using System;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A construction stage paid by holding its tool: the tool in the main hand, the hits of the
/// hold, the wear, and the materials taken at the last hit.</summary>
public class ConstructionToolTests {
  private sealed class StubBlockEntity : BlockEntity;

  private const float Stroke = 1.05f;

  private sealed class Site {
    public readonly TestWorld World = new();
    public readonly IAnimationManager Animations =
      Substitute.For<IAnimationManager>();
    public readonly ExRightClickConstructable Behavior;
    public readonly TestPlayer Payer;
    public readonly Item Hammer;
    public readonly Item Tongs;
    public readonly Item Rod;

    public Site(
      System.Func<
        Definitions.ConstructionStages,
        Definitions.ConstructionStages
      >? stages = null,
      JObject? properties = null,
      bool client = false
    ) {
      Hammer = World.RegisterItem("game:hammer-iron");
      Hammer.Durability = 100;
      Tongs = World.RegisterItem("test:tongs-iron");
      Tongs.Durability = 100;
      Rod = World.RegisterItem("test:rod");
      ICoreAPI api = client ? World.ClientApi : World.Api;
      var be = new StubBlockEntity {
        Block = TestBlocks.Configure(new Block(), "stub:toolwork", 5101),
        Pos = new BlockPos(0, 0, 0, 0),
      };
      be.Initialize(api);
      Behavior = new ExRightClickConstructable(be);
      Behavior.Initialize(
        api,
        new JsonObject(
          properties
            ?? (
              stages
              ?? (
                s =>
                  s.Stage(_ => { })
                    .Stage(t =>
                      t.Tool("game:hammer-*", 3).Require("test:rod", 1)
                    )
              )
            )(new Definitions.ConstructionStages())
              .Build()
        )
      );
      Payer = World.Player();
      Payer.GameMode = EnumGameMode.Survival;
      Payer.Entity.AnimManager = Animations;
      Payer.Hotbar[1].Itemstack = new ItemStack(Rod, 2);
      Payer.Hold(new ItemStack(Hammer));
    }

    public int Stage => Behavior.CurrentCompletedStage;

    public int Durability =>
      Hammer.GetRemainingDurability(Payer.ActiveSlot.Itemstack);

    public int TongsDurability =>
      Tongs.GetRemainingDurability(Payer.ActiveSlot.Itemstack);

    public int Rods => Payer.Hotbar[1].Itemstack?.StackSize ?? 0;

    public bool Start(TestPlayer? by = null) {
      EnumHandling handling = EnumHandling.PassThrough;
      return Behavior.OnBlockInteractStart(
        World.World,
        (by ?? Payer).Player,
        new BlockSelection { Position = new BlockPos(0, 0, 0, 0) },
        ref handling
      );
    }

    public bool Step(float seconds, TestPlayer? by = null) {
      EnumHandling handling = EnumHandling.PassThrough;
      return Behavior.OnBlockInteractStep(
        seconds,
        World.World,
        (by ?? Payer).Player,
        new BlockSelection { Position = new BlockPos(0, 0, 0, 0) },
        ref handling
      );
    }

    public void Stop(float seconds, TestPlayer? by = null) {
      EnumHandling handling = EnumHandling.PassThrough;
      Behavior.OnBlockInteractStop(
        seconds,
        World.World,
        (by ?? Payer).Player,
        new BlockSelection { Position = new BlockPos(0, 0, 0, 0) },
        ref handling
      );
    }
  }

  // Fails when a plain click pays a tool stage.
  [Fact]
  public void A_plain_click_starts_the_hold_and_pays_nothing() {
    var site = new Site();

    Assert.True(site.Start());

    Assert.Equal(0, site.Stage);
    Assert.Equal(2, site.Rods);
    Assert.True(site.Behavior.Construction.Holding);
  }

  // Fails when the stage is built before its last hit, or the hit count is not the stage's.
  [Fact]
  public void The_stage_is_built_on_its_last_hit_and_takes_the_materials_then() {
    var site = new Site();
    site.Start();

    Assert.True(site.Step(Stroke + 0.05f));
    Assert.True(site.Step((2 * Stroke) + 0.05f));
    Assert.Equal(0, site.Stage);
    Assert.Equal(2, site.Rods);
    Assert.Equal(100, site.Durability);

    Assert.False(site.Step((3 * Stroke) + 0.05f));
    Assert.Equal(1, site.Stage);
    Assert.Equal(1, site.Rods);
    Assert.False(site.Behavior.Construction.Holding);
  }

  // Fails when the hits default to anything but two for a stage that names a tool and no hits.
  [Fact]
  public void A_stage_in_JSON_naming_only_its_tool_takes_two_hits() {
    var site = new Site(
      properties: JObject.Parse(
        """{ "stages": [ {}, { "tool": "game:hammer-*" } ] }"""
      )
    );
    site.Start();

    Assert.True(site.Step(Stroke + 0.05f));
    Assert.False(site.Step((2 * Stroke) + 0.05f));
    Assert.Equal(1, site.Stage);
  }

  // Fails when the tool is not charged one durability at the last hit, or is charged earlier.
  [Fact]
  public void The_last_hit_wears_the_tool_by_one() {
    var site = new Site();
    site.Start();
    site.Step((2 * Stroke) + 0.05f);
    Assert.Equal(100, site.Durability);

    site.Step((3 * Stroke) + 0.05f);

    Assert.Equal(99, site.Durability);
  }

  // Fails when a stage that names a tool is started with another stack, or with none, in the main hand.
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Without_the_tool_in_the_main_hand_the_stage_refuses(
    bool otherTool
  ) {
    var site = new Site();
    site.Payer.Hold(otherTool ? new ItemStack(site.Tongs) : null);

    Assert.False(site.Start());

    Assert.False(site.Behavior.Construction.Holding);
    Assert.False(site.Step(Stroke + 0.05f));
    Assert.Equal(0, site.Stage);
    Assert.Equal(2, site.Rods);
  }

  // Fails when the materials are checked only at the last hit, so the player swings for nothing.
  [Fact]
  public void A_stage_without_its_materials_refuses_at_the_click() {
    var site = new Site();
    site.Payer.Hotbar[1].Itemstack = null;

    Assert.False(site.Start());

    Assert.False(site.Behavior.Construction.Holding);
  }

  // Fails when hits are dropped at the let-go, or the swing left unfinished is counted.
  [Fact]
  public void Hits_survive_the_player_letting_go() {
    var site = new Site();
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Step(Stroke + 0.9f);
    site.Stop(Stroke + 0.9f);

    Assert.True(site.Start());
    Assert.True(site.Step(Stroke + 0.05f));
    Assert.Equal(0, site.Stage);
    Assert.False(site.Step((2 * Stroke) + 0.05f));
    Assert.Equal(1, site.Stage);
  }

  // Fails when hits counted on a stage that another click then built carry to the next stage.
  [Fact]
  public void Hits_counted_on_a_stage_built_another_way_do_not_carry_to_the_next() {
    var site = new Site(s =>
      s.Stage(_ => { })
        .Stage(t => t.Tool("game:hammer-*", 3))
        .Stage(t => t.Tool("game:hammer-*", 3))
    );
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Stop(Stroke + 0.05f);
    site.Payer.GameMode = EnumGameMode.Creative;
    site.Payer.CtrlHeld = true;
    site.Start();
    Assert.Equal(1, site.Stage);
    site.Payer.GameMode = EnumGameMode.Survival;
    site.Payer.CtrlHeld = false;

    site.Start();

    Assert.True(site.Step(Stroke + 0.05f));
    Assert.True(site.Step((2 * Stroke) + 0.05f));
    Assert.Equal(1, site.Stage);
  }

  // Fails when a hold goes on after the tool leaves the main hand.
  [Fact]
  public void A_hold_ends_when_the_tool_leaves_the_main_hand() {
    var site = new Site();
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Payer.Hold(new ItemStack(site.Tongs));

    Assert.False(site.Step((3 * Stroke) + 0.05f));

    Assert.Equal(0, site.Stage);
    Assert.False(site.Behavior.Construction.Holding);
  }

  // Fails when a hold that the materials left at the last hit still builds the stage.
  [Fact]
  public void A_hold_whose_materials_are_gone_at_the_last_hit_builds_nothing() {
    var site = new Site();
    site.Start();
    site.Payer.Hotbar[1].Itemstack = null;

    Assert.False(site.Step((3 * Stroke) + 0.05f));

    Assert.Equal(0, site.Stage);
    Assert.Equal(100, site.Durability);
  }

  // Fails when a creative player holding Ctrl needs the tool or a hold.
  [Fact]
  public void A_creative_player_holding_ctrl_builds_at_the_click_without_the_tool() {
    var site = new Site();
    site.Payer.GameMode = EnumGameMode.Creative;
    site.Payer.CtrlHeld = true;
    site.Payer.Hold(null);

    Assert.True(site.Start());

    Assert.Equal(1, site.Stage);
    Assert.Equal(2, site.Rods);
  }

  // Fails when the swings play the tool's default instead of the stage's own animation and sound, or
  // play them at another count than the hits.
  [Fact]
  public void Each_hit_swings_the_stages_animation_and_strikes_its_sound() {
    var site = new Site(s =>
      s.Stage(_ => { })
        .Stage(t => t.Tool("game:hammer-*", 3, "swing", "sounds/effect/clang"))
    );
    site.Start();
    foreach (float at in new[] { 0.5f, 1.2f, 1.6f, 2.3f, 2.8f, 3.4f })
      site.Step(at);

    site.Animations.Received(3).StartAnimation("swing");
    site.World.World.Received(3)
      .PlaySoundAt(
        Arg.Is<AssetLocation>(l => l.Path == "sounds/effect/clang"),
        site.Payer.Entity,
        null,
        Arg.Any<float>(),
        Arg.Any<float>(),
        Arg.Any<float>()
      );
  }

  // Fails when a stage naming no animation or sound ignores the tool's registry default.
  [Fact]
  public void A_stage_naming_none_uses_the_tools_default_animation() {
    var site = new Site();
    site.Start();
    site.Step(0.5f);

    site.Animations.Received(1).StartAnimation("hammerhit");
  }

  // Fails when the last hit lets go of the swing's animation no more than the let-go does.
  [Fact]
  public void Letting_go_stops_the_swing_animation() {
    var site = new Site();
    site.Start();
    site.Step(0.5f);

    site.Stop(0.5f);

    site.Animations.Received(1).StopAnimation("hammerhit");
    Assert.False(site.Behavior.Construction.Holding);
  }

  // Fails when a client builds the stage, takes the materials or wears the tool, or plays the strike
  // sound the server plays for everyone.
  [Fact]
  public void A_client_swings_the_animation_and_decides_nothing() {
    var site = new Site(client: true);
    Assert.True(site.Start());

    site.Step(Stroke + 0.05f);
    site.Step((2 * Stroke) + 0.05f);
    Assert.False(site.Step((3 * Stroke) + 0.05f));

    Assert.Equal(0, site.Stage);
    Assert.Equal(2, site.Rods);
    Assert.Equal(100, site.Durability);
    site.Animations.Received(3).StartAnimation("hammerhit");
    site.World.World.DidNotReceiveWithAnyArgs()
      .PlaySoundAt(default(AssetLocation)!, default(Entity)!, null, 0f, 0f, 0f);
  }

  // Fails when a client counts a finished step before the server's count reaches it, so it asks for
  // the second step's tool while the server still wants the first.
  [Fact]
  public void A_client_counts_no_step_of_its_own() {
    var site = new Site(TwoSteps, client: true);
    Assert.True(site.Start());

    Assert.False(site.Step((2 * Stroke) + 0.05f));

    Assert.Equal(0, site.Behavior.Construction.StepsPaid);
    Assert.True(site.Start());
  }

  // Fails when the help does not offer the stage's tools beside its materials.
  [Fact]
  public void The_interaction_help_names_the_tool() {
    var site = new Site();

    WorldInteraction[] help = site.Behavior.GetConstructionInteractionHelp()!;

    Assert.Equal("exlib:rcc-hold-tool", help[0].ActionLangCode);
    Assert.Equal(
      ["game:hammer-iron"],
      help[0].Itemstacks.Select(s => s.Collectible.Code.ToString())
    );
    Assert.Equal(2, help.Length);
  }

  // Fails when the builder writes the tool and hits under other keys, or admits a hit count below 1.
  [Fact]
  public void The_builder_writes_the_tool_stage_and_refuses_no_hits() {
    JObject built = new Definitions.ConstructionStages()
      .Stage(s => s.Tool("game:hammer-*", 4, "swing", "sounds/x"))
      .Build();

    JToken stage = built["stages"]![0]!;
    Assert.Equal("game:hammer-*", (string?)stage["tool"]);
    Assert.Equal(4, (int?)stage["hits"]);
    Assert.Equal("swing", (string?)stage["animation"]);
    Assert.Equal("sounds/x", (string?)stage["sound"]);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      new Definitions.ConstructionStage().Tool("game:hammer-*", 0)
    );
  }

  private static Definitions.ConstructionStages TwoSteps(
    Definitions.ConstructionStages s
  ) =>
    s.Stage(_ => { })
      .Stage(t =>
        t.Tool("game:hammer-*", 2)
          .Tool("test:tongs-*", 2)
          .Require("test:rod", 1)
      );

  // Fails when the first step's tool alone builds a two-step stage.
  [Fact]
  public void A_two_step_stage_is_not_built_by_its_first_tool_alone() {
    var site = new Site(TwoSteps);
    site.Start();

    site.Step(Stroke + 0.05f);
    Assert.False(site.Step((2 * Stroke) + 0.05f));

    Assert.Equal(0, site.Stage);
    Assert.Equal(2, site.Rods);
    Assert.False(site.Behavior.Construction.Holding);
    Assert.Equal(1, site.Behavior.Construction.StepsPaid);
  }

  // Fails when the second step is asked before the first has its hits, or accepts the first step's tool.
  [Fact]
  public void The_second_step_is_asked_only_once_the_first_is_done() {
    var site = new Site(TwoSteps);
    site.Payer.Hold(new ItemStack(site.Tongs));
    Assert.False(site.Start());
    site.Payer.Hold(new ItemStack(site.Hammer));
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Step((2 * Stroke) + 0.05f);

    Assert.False(site.Start());
    site.Payer.Hold(new ItemStack(site.Tongs));
    Assert.True(site.Start());
    Assert.Equal(0, site.Stage);
  }

  // Fails when the materials or the stage's elements come at the end of a step that is not the last.
  [Fact]
  public void The_materials_are_taken_and_the_stage_built_on_the_last_hit_of_the_last_step() {
    var site = new Site(TwoSteps);
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Step((2 * Stroke) + 0.05f);
    Assert.Equal(2, site.Rods);
    site.Payer.Hold(new ItemStack(site.Tongs));
    site.Start();
    Assert.True(site.Step(Stroke + 0.05f));
    Assert.Equal(2, site.Rods);
    Assert.Equal(0, site.Stage);

    Assert.False(site.Step((2 * Stroke) + 0.05f));

    Assert.Equal(1, site.Stage);
    Assert.Equal(1, site.Rods);
    Assert.Equal(0, site.Behavior.Construction.StepsPaid);
  }

  // Fails when a step wears no tool, or wears the stage's first tool at the end.
  [Fact]
  public void Each_step_wears_its_own_tool_at_its_last_hit() {
    var site = new Site(TwoSteps);
    site.Start();
    site.Step(Stroke + 0.05f);
    Assert.Equal(100, site.Durability);
    site.Step((2 * Stroke) + 0.05f);
    Assert.Equal(99, site.Durability);
    site.Payer.Hold(new ItemStack(site.Tongs));
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Step((2 * Stroke) + 0.05f);

    Assert.Equal(99, site.TongsDurability);
  }

  // Fails when a step's hits are dropped at the let-go, or carry into the next step.
  [Fact]
  public void The_hits_of_a_step_survive_a_let_go_and_do_not_carry_to_the_next() {
    var site = new Site(TwoSteps);
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Stop(Stroke + 0.05f);
    site.Start();
    Assert.False(site.Step(Stroke + 0.05f));
    site.Payer.Hold(new ItemStack(site.Tongs));
    site.Start();

    Assert.True(site.Step(Stroke + 0.05f));
    Assert.Equal(0, site.Stage);
  }

  // Fails when two players at one structure share a stroke clock or a let-go.
  [Fact]
  public void Two_players_holds_keep_their_own_stroke_clocks() {
    var site = new Site();
    site.Payer.Entity.EntityId = 1;
    TestPlayer other = site.World.Player("other", "Other");
    other.Entity.EntityId = 2;
    other.Entity.AnimManager = site.Animations;
    other.Hold(new ItemStack(site.Hammer));
    site.Start();
    site.Step(Stroke + 0.05f);

    other.GameMode = EnumGameMode.Survival;
    site.Start(other);
    site.Stop(0.5f, other);

    Assert.True(site.Step((2 * Stroke) + 0.05f));
    Assert.False(site.Step((3 * Stroke) + 0.05f));
    Assert.Equal(1, site.Stage);
  }

  // Fails when the refusal for the wrong tool is not shown to a client player with the tool's name.
  [Fact]
  public void A_client_holding_the_wrong_tool_is_told_the_tool_by_name() {
    var site = new Site(client: true);
    var clientPlayer = Substitute.For<IClientPlayer>();
    clientPlayer.PlayerUID.Returns("test");
    site.World.AddPlayer("test", clientPlayer);
    site.Payer.Hold(new ItemStack(site.Tongs));

    Assert.False(site.Start());

    site.World.ClientApi.Received(1)
      .TriggerIngameError(Arg.Any<object>(), "missingtool", Arg.Any<string>());
    TestLang
      .Service.Received()
      .Get(
        "exlib:ingameerror-construction-missingtool",
        Arg.Is<object[]>(args =>
          args.Length == 1 && args[0].ToString()!.Contains("hammer")
        )
      );
  }

  // Fails when the help names the first step's tool after it is done.
  [Fact]
  public void The_interaction_help_names_the_current_steps_tool() {
    var site = new Site(TwoSteps);
    Assert.Equal(
      ["game:hammer-iron"],
      site.Behavior.GetConstructionInteractionHelp()![0]
        .Itemstacks.Select(s => s.Collectible.Code.ToString())
    );
    site.Start();
    site.Step(Stroke + 0.05f);
    site.Step((2 * Stroke) + 0.05f);

    WorldInteraction[] help = site.Behavior.GetConstructionInteractionHelp()!;

    Assert.Equal(
      ["test:tongs-iron"],
      help[0].Itemstacks.Select(s => s.Collectible.Code.ToString())
    );
  }

  // Fails when `steps` in JSON is not read after the stage's own tool, or its hits and tools are lost.
  [Fact]
  public void A_stage_in_JSON_lists_its_steps_in_order() {
    var site = new Site(
      properties: JObject.Parse(
        """
        { "stages": [ {}, { "steps": [
          { "tool": "game:hammer-*", "hits": 1 },
          { "tool": "test:tongs-*" } ] } ] }
        """
      )
    );
    site.Start();
    Assert.False(site.Step(Stroke + 0.05f));
    Assert.Equal(1, site.Behavior.Construction.StepsPaid);
    site.Payer.Hold(new ItemStack(site.Tongs));
    site.Start();
    Assert.True(site.Step(Stroke + 0.05f));
    Assert.False(site.Step((2 * Stroke) + 0.05f));
    Assert.Equal(1, site.Stage);
  }

  // Fails when `hits: 0`, or a step without a tool, loads.
  [Theory]
  [InlineData("""{ "tool": "game:hammer-*", "hits": 0 }""")]
  [InlineData("""{ "steps": [ { "tool": "game:hammer-*", "hits": 0 } ] }""")]
  [InlineData("""{ "steps": [ { "hits": 2 } ] }""")]
  public void A_tool_step_with_no_hits_or_no_tool_is_refused_at_load(
    string stage
  ) {
    Assert.Throws<InvalidOperationException>(() =>
      new Site(
        properties: JObject.Parse($$"""{ "stages": [ {}, {{stage}} ] }""")
      )
    );
  }

  // Fails when a second Tool call replaces the first instead of appending a step.
  [Fact]
  public void The_builder_appends_a_step_for_each_later_tool_call() {
    JObject built = new Definitions.ConstructionStages()
      .Stage(s =>
        s.Tool("game:hammer-*", 4).Tool("test:tongs-*", 1, "swing", "sounds/x")
      )
      .Build();

    JToken stage = built["stages"]![0]!;
    Assert.Equal("game:hammer-*", (string?)stage["tool"]);
    Assert.Equal(4, (int?)stage["hits"]);
    JToken step = Assert.Single(stage["steps"]!);
    Assert.Equal("test:tongs-*", (string?)step["tool"]);
    Assert.Equal(1, (int?)step["hits"]);
    Assert.Equal("swing", (string?)step["animation"]);
    Assert.Equal("sounds/x", (string?)step["sound"]);
  }
}
