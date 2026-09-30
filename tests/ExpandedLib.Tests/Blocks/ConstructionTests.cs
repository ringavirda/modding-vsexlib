using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Tests <see cref="ExRightClickConstructable.GatesProduction"/> and readiness, one material per
/// stage, <see cref="ExRccSettings"/>, and <see cref="ConstructedAnimator.IsConstructed"/>.
/// </summary>
public class ConstructionTests {
  /// <summary>Minimal concrete block entity: only used to host a behavior under test.</summary>
  private sealed class StubBlockEntity : BlockEntity;

  #region GatesProduction / readiness

  private static ExRightClickConstructable Behavior(
    TestWorld world,
    bool? gatesProduction
  ) {
    var block = TestBlocks.Configure(new Block(), "stub:rccblock", 4999);
    var be = new StubBlockEntity {
      Block = block,
      Pos = new BlockPos(0, 0, 0, 0),
    };
    be.Initialize(world.Api);
    var behavior = new ExRightClickConstructable(be);

    var json = new JObject {
      // Two stages: IsComplete compares CurrentCompletedStage against Stages.Length - 1.
      ["stages"] = new JArray { new JObject(), new JObject() },
    };
    if (gatesProduction.HasValue)
      json["gatesProduction"] = gatesProduction.Value;

    behavior.Initialize(world.Api, new JsonObject(json));
    return behavior;
  }

  [Fact]
  public void GatesProduction_defaults_to_true_when_the_property_is_absent() {
    var behavior = Behavior(new TestWorld(), gatesProduction: null);

    Assert.True(behavior.GatesProduction);
  }

  [Fact]
  public void GatesProduction_reads_false_from_JSON() {
    var behavior = Behavior(new TestWorld(), gatesProduction: false);

    Assert.False(behavior.GatesProduction);
  }

  [Fact]
  public void An_unfinished_construction_is_not_ready_when_it_gates_production() {
    var behavior = Behavior(new TestWorld(), gatesProduction: true);

    Assert.False(behavior.IsComplete);
    Assert.False(behavior.IsReadyToProduce);
    Assert.True(behavior.StopsProductionWhenNotReady);
  }

  [Fact]
  public void An_unfinished_construction_is_always_ready_when_it_opts_out_of_gating() {
    var behavior = Behavior(new TestWorld(), gatesProduction: false);

    Assert.False(behavior.IsComplete);
    Assert.True(behavior.IsReadyToProduce);
    Assert.False(behavior.StopsProductionWhenNotReady);
  }

  #endregion

  #region ExRccSettings

  private static string FreshDomain() =>
    "rcctest-" + System.Guid.NewGuid().ToString("N")[..8];

  [Fact]
  public void An_unregistered_domain_leaves_the_ratio_unset() {
    Assert.Null(ExRccSettings.BrokenDropsRatio(FreshDomain()));
  }

  [Fact]
  public void A_registered_domain_reads_its_getter_live_on_every_call() {
    string domain = FreshDomain();
    float current = 1f;
    ExRccSettings.RegisterBrokenDropsRatio(domain, () => current);

    Assert.Equal(1f, ExRccSettings.BrokenDropsRatio(domain));
    current = 0.25f;
    Assert.Equal(0.25f, ExRccSettings.BrokenDropsRatio(domain));
  }

  #endregion

  #region One material per stage

  private sealed record Site(
    TestWorld World,
    ExRightClickConstructable Behavior,
    TestPlayer Payer
  );

  // Stage 1 takes two plates and a rod, each storing metal; stage 2 asks for nothing; stage 3 takes
  // a rod of the stored metal.
  private static Site Metalwork(
    EnumGameMode mode = EnumGameMode.Survival,
    bool ctrl = false
  ) {
    var world = new TestWorld();
    foreach (string metal in new[] { "iron", "steel" })
      foreach (string part in new[] { "metalplate", "rod" })
        world.RegisterItem($"game:{part}-{metal}").VariantStrict["metal"] = metal;
    var be = new StubBlockEntity {
      Block = TestBlocks.Configure(new Block(), "stub:metalwork", 5001),
      Pos = new BlockPos(0, 0, 0, 0),
    };
    be.Initialize(world.Api);
    var behavior = new ExRightClickConstructable(be);
    JObject properties = new Definitions.ConstructionStages()
      .Stage(_ => { })
      .Stage(s => s.RequireMetalPlate("stub", 2).RequireMetalRod("stub", 1))
      .Stage(s => s.AddElements("Frame"))
      .Stage(s => s.RequireMetalRod("stub", 1))
      .Build();
    behavior.Initialize(world.Api, new JsonObject(properties));
    TestPlayer payer = world.Player();
    payer.GameMode = mode;
    payer.CtrlHeld = ctrl;
    return new Site(world, behavior, payer);
  }

  private static void Offer(Site site, params string[] stacks) {
    for (int i = 0; i < site.Payer.Hotbar.Count; i++)
      site.Payer.Hotbar[i].Itemstack =
        i < stacks.Length
          ? new ItemStack(
            site.World.GetItem(new AssetLocation(stacks[i].Split(' ')[0])),
            int.Parse(stacks[i].Split(' ')[1])
          )
          : null;
  }

  /// <summary>Right-clicks the construction and returns its completed stage.</summary>
  private static int Interact(Site site) {
    EnumHandling handling = EnumHandling.PassThrough;
    site.Behavior.OnBlockInteractStart(
      site.World.World,
      site.Payer.Player,
      new BlockSelection { Position = new BlockPos(0, 0, 0, 0) },
      ref handling
    );
    return (int)
      ReflectionHelpers.GetField(Rcc(site.Behavior), "CurrentCompletedStage")!;
  }

  private static object Rcc(ExRightClickConstructable behavior) =>
    ReflectionHelpers.GetField(behavior, "rcc")!;

  private static string? StoredMetal(ExRightClickConstructable behavior) =>
    (
      (Dictionary<string, string>)
        ReflectionHelpers.GetField(Rcc(behavior), "StoredWildCards")!
    ).GetValueOrDefault("metal");

  private static int Held(Site site) => site.Payer.Hotbar.Sum(s => s.StackSize);

  // Fails when the constructable admits a stored key in two variants inside the stage that stores
  // it: the plates are then taken as steel and iron.
  [Fact]
  public void A_stage_refuses_one_ingredient_paid_in_two_metals() {
    Site site = Metalwork();
    Offer(
      site,
      "game:metalplate-steel 1",
      "game:metalplate-iron 1",
      "game:rod-iron 1"
    );

    Assert.Equal(0, Interact(site));
    Assert.Equal(3, Held(site));
    Assert.Null(StoredMetal(site.Behavior));
  }

  // Fails when the constructable admits a stored key in two variants inside the stage that stores
  // it: the plates are then taken as iron and the rod as steel.
  [Fact]
  public void A_stage_refuses_two_ingredients_paid_in_two_metals() {
    Site site = Metalwork();
    Offer(site, "game:metalplate-iron 2", "game:rod-steel 1");

    Assert.Equal(0, Interact(site));
    Assert.Equal(3, Held(site));
  }

  // Fails when a second slot of the metal already taken counts as a second metal.
  [Fact]
  public void A_stage_paid_in_one_metal_takes_it_and_stores_it() {
    Site site = Metalwork();
    Offer(
      site,
      "game:metalplate-iron 1",
      "game:metalplate-iron 1",
      "game:rod-iron 1"
    );

    Assert.Equal(1, Interact(site));
    Assert.Equal(0, Held(site));
    Assert.Equal("iron", StoredMetal(site.Behavior));
  }

  // Fails when a creative player without Ctrl is let through as one holding it.
  [Fact]
  public void A_creative_player_without_ctrl_is_refused_two_metals() {
    Site site = Metalwork(EnumGameMode.Creative);
    Offer(site, "game:metalplate-iron 2", "game:rod-steel 1");

    Assert.Equal(0, Interact(site));
    Assert.Equal(3, Held(site));
  }

  // Fails when a creative player holding Ctrl is refused: the game charges that player nothing.
  [Fact]
  public void A_creative_player_holding_ctrl_builds_without_paying() {
    Site site = Metalwork(EnumGameMode.Creative, ctrl: true);
    Offer(site, "game:metalplate-iron 2", "game:rod-steel 1");

    Assert.Equal(1, Interact(site));
    Assert.Equal(3, Held(site));
    Assert.Equal("iron", StoredMetal(site.Behavior));
  }

  // Fails when a stage asking for nothing is read for ingredients: its null list throws.
  [Fact]
  public void A_stage_asking_for_nothing_is_built_whatever_the_hotbar_holds() {
    Site site = Metalwork();
    Offer(site, "game:metalplate-iron 2", "game:rod-iron 1");
    Assert.Equal(1, Interact(site));
    Offer(site, "game:metalplate-iron 1", "game:metalplate-steel 1");

    Assert.Equal(2, Interact(site));
  }

  // Fails when a complete construction reads the stage past its last: the index is out of range.
  [Fact]
  public void A_complete_construction_takes_nothing_more() {
    Site site = Metalwork();
    Offer(site, "game:metalplate-iron 2", "game:rod-iron 1");
    Interact(site);
    Interact(site);
    Offer(site, "game:rod-iron 1");
    Assert.Equal(3, Interact(site));
    Offer(site, "game:rod-iron 1");

    Assert.Equal(3, Interact(site));
    Assert.Equal(1, Held(site));
  }

  #endregion

  #region Save data, sound and drops

  // Fails when the behaviour reads other keys than the ones the game's construction writes: the
  // stage and the stored metal are then lost.
  [Fact]
  public void A_tree_written_by_the_games_construction_loads_at_its_stage() {
    Site site = Metalwork();
    var tree = new TreeAttribute();
#if GAME_GE_1_22
    var vanilla = new RightClickConstruction { CurrentCompletedStage = 2 };
    vanilla.StoredWildCards["metal"] = "iron";
    vanilla.ToTreeAttributes(tree);
#else
    var wildcards = new TreeAttribute();
    wildcards["metal"] = new StringAttribute("iron");
    tree["wildcards"] = wildcards;
    tree.SetInt("currentStage", 2);
#endif

    site.Behavior.FromTreeAttributes(tree, site.World.World);

    Assert.Equal(2, site.Behavior.CurrentCompletedStage);
    Assert.Equal("iron", StoredMetal(site.Behavior));
  }

  // Fails when the behaviour writes other keys than the ones the game's construction reads.
  [Fact]
  public void The_tree_the_behaviour_writes_holds_the_games_keys() {
    Site site = Metalwork();
    ReflectionHelpers.SetField(Rcc(site.Behavior), "CurrentCompletedStage", 2);
    (
      (Dictionary<string, string>)
        ReflectionHelpers.GetField(Rcc(site.Behavior), "StoredWildCards")!
    )["metal"] = "steel";
    var tree = new TreeAttribute();

    site.Behavior.ToTreeAttributes(tree);

    Assert.Equal(2, tree.GetInt("currentStage", -1));
    Assert.Equal(
      "steel",
      tree.GetTreeAttribute("wildcards")?.GetString("metal")
    );
  }

  // Fails when the payment plays nothing, or plays the sound once per slot paid from: a stage paid
  // with a block from two slots plays that block's place sound once.
  [Fact]
  public void A_stage_paid_with_a_block_plays_its_place_sound_once_at_the_block() {
    var world = new TestWorld();
    var plank = TestBlocks.Configure(new Block(), "game:plank-oak", 6001);
    plank.Sounds = new BlockSounds();
#if GAME_GE_1_22
    plank.Sounds.Place = new SoundAttributes(
      new AssetLocation("game:sounds/block/planks"),
      true
    );
#else
    plank.Sounds.Place = new AssetLocation("game:sounds/block/planks");
#endif
    ReflectionHelpers.SetField(plank, "api", world.Api);
    world.Register(plank);
    var be = new StubBlockEntity {
      Block = TestBlocks.Configure(new Block(), "stub:planked", 6002),
      Pos = new BlockPos(3, 4, 5, 0),
    };
    be.Initialize(world.Api);
    var behavior = new ExRightClickConstructable(be);
    behavior.Initialize(
      world.Api,
      new JsonObject(
        new Definitions.ConstructionStages()
          .Stage(_ => { })
          .Stage(s => s.Require("game:plank-oak", 2, type: "block"))
          .Build()
      )
    );
    TestPlayer payer = world.Player();
    payer.GameMode = EnumGameMode.Survival;
    payer.Hotbar[0].Itemstack = new ItemStack(plank, 1);
    payer.Hotbar[1].Itemstack = new ItemStack(plank, 1);
    world.World.ClearReceivedCalls();

    EnumHandling handling = EnumHandling.PassThrough;
    behavior.OnBlockInteractStart(
      world.World,
      payer.Player,
      new BlockSelection { Position = be.Pos },
      ref handling
    );

    Assert.Equal(1, behavior.CurrentCompletedStage);
    world
      .World.Received(1)
      .PlaySoundAt(
        Arg.Is<AssetLocation>(l => l.ToString() == "game:sounds/block/planks"),
        3.5,
        4.5,
        5.5,
        payer.Player,
        Arg.Any<bool>(),
        Arg.Any<float>(),
        Arg.Any<float>()
      );
  }

  // Fails when GetConstructionDrops loses NoInlining: ppex patches it by name.
  [Fact]
  public void GetConstructionDrops_is_not_inlined() {
    Assert.True(
      typeof(ExRightClickConstructable)
        .GetMethod(nameof(ExRightClickConstructable.GetConstructionDrops))!
        .MethodImplementationFlags.HasFlag(MethodImplAttributes.NoInlining)
    );
  }

  // Fails when ExRightClickConstruction.GetDrops loses NoInlining: ppex patches it by name.
  [Fact]
  public void The_construction_GetDrops_is_not_inlined() {
    Assert.True(
      typeof(ExRightClickConstruction)
        .GetMethod(nameof(ExRightClickConstruction.GetDrops))!
        .MethodImplementationFlags.HasFlag(MethodImplAttributes.NoInlining)
    );
  }

  #endregion

  #region ConstructedAnimator.IsConstructed

  private static ExRightClickConstructable CompletedRcc(BlockEntity be) {
    var rcc = new ExRightClickConstructable(be);
    var construction = new ExRightClickConstruction {
      Stages = [new ExConstructionStage()],
      CurrentCompletedStage = 0,
    };
    ReflectionHelpers.SetField(rcc, "rcc", construction);
    return rcc;
  }

  [Fact]
  public void IsConstructed_is_false_before_a_behavior_is_resolved() {
    var animator = new ConstructedAnimator(new StubBlockEntity(), () => "key");

    Assert.False(animator.IsConstructed);
  }

  [Fact]
  public void IsConstructed_follows_the_resolved_behaviors_own_IsComplete() {
    var be = new StubBlockEntity {
      Block = TestBlocks.Configure(new Block(), "stub:rccblock2", 5000),
    };
    var animator = new ConstructedAnimator(be, () => "key");
    ReflectionHelpers.SetField(animator, "_rcc", CompletedRcc(be));

    Assert.True(animator.IsConstructed);
  }

  #endregion
}
