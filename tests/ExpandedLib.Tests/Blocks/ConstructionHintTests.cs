using System;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Tests how often the construction hint scans the world's collectibles.
/// </summary>
public class ConstructionHintTests {
  private sealed class StubBlockEntity : BlockEntity;

  private static TestWorld MetalWorld() {
    var world = new TestWorld();
    foreach (string metal in new[] { "iron", "steel" })
      foreach (string part in new[] { "metalplate", "rod" })
        world.RegisterItem($"game:{part}-{metal}").VariantStrict["metal"] = metal;
    return world;
  }

  // Stage 1 takes two plates and a rod, each storing metal; stage 2 takes a rod of the stored
  // metal; stage 3 asks for nothing.
  private static ExRightClickConstructable Construction(
    TestWorld world,
    ICoreAPI api,
    BlockPos pos
  ) {
    var be = new StubBlockEntity {
      Block = TestBlocks.Configure(new Block(), "stub:hintwork", 5002),
      Pos = pos,
    };
    be.Initialize(api);
    var behavior = new ExRightClickConstructable(be);
    var properties = new Definitions.ConstructionStages()
      .Stage(_ => { })
      .Stage(s => s.RequireMetalPlate("stub", 2).RequireMetalRod("stub", 1))
      .Stage(s => s.RequireMetalRod("stub", 1))
      .Stage(s => s.AddElements("Frame"))
      .Build();
    behavior.Initialize(api, new JsonObject(properties));
    return behavior;
  }

  private static int Scans(IWorldAccessor world) =>
    world
      .ReceivedCalls()
      .Count(c => c.GetMethodInfo().Name == "get_Collectibles");

  private static WorldInteraction[]? Hint(ExRightClickConstructable behavior) {
    return behavior.GetConstructionInteractionHelp();
  }

  private static string[] Listed(WorldInteraction interaction) =>
    interaction
      .Itemstacks.Select(s => $"{s.Collectible.Code} {s.StackSize}")
      .OrderBy(s => s)
      .ToArray();

  // Fails when the hint rescans the collectibles on every request instead of once per distinct
  // filled ingredient in a world.
  [Fact]
  public void Repeated_hint_requests_scan_once_per_ingredient() {
    TestWorld world = MetalWorld();
    ExRightClickConstructable behavior = Construction(
      world,
      world.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    world.ClientApi.World.ClearReceivedCalls();

    WorldInteraction[]? hint = null;
    for (int i = 0; i < 5; i++)
      hint = Hint(behavior);

    Assert.Equal(2, Scans(world.ClientApi.World));
    Assert.NotNull(hint);
    Assert.Equal(2, hint.Length);
    Assert.Equal(
      new[] { "game:metalplate-iron 2", "game:metalplate-steel 2" },
      Listed(hint[0])
    );
    Assert.Equal(
      new[] { "game:rod-iron 1", "game:rod-steel 1" },
      Listed(hint[1])
    );
  }

  // Fails when a paid stage builds the hint, as vanilla's OnInteract does on the server.
  [Fact]
  public void A_paid_stage_scans_nothing() {
    TestWorld world = MetalWorld();
    ExRightClickConstructable behavior = Construction(
      world,
      world.Api,
      new BlockPos(0, 0, 0, 0)
    );
    TestPlayer payer = world.Player();
    payer.GameMode = EnumGameMode.Survival;
    payer.Hotbar[0].Itemstack = new ItemStack(
      world.GetItem(new AssetLocation("game:metalplate-iron")),
      2
    );
    payer.Hotbar[1].Itemstack = new ItemStack(
      world.GetItem(new AssetLocation("game:rod-iron")),
      1
    );
    world.World.ClearReceivedCalls();

    EnumHandling handling = EnumHandling.PassThrough;
    behavior.OnBlockInteractStart(
      world.World,
      payer.Player,
      new BlockSelection { Position = behavior.Pos },
      ref handling
    );

    var rcc = ReflectionHelpers.GetField(behavior, "rcc")!;
    Assert.Equal(
      1,
      (int)ReflectionHelpers.GetField(rcc, "CurrentCompletedStage")!
    );
    Assert.Equal(0, Scans(world.World));
  }

  // Fails when the cache is per construction instead of per world.
  [Fact]
  public void Two_constructions_in_one_world_share_the_scan() {
    TestWorld world = MetalWorld();
    ExRightClickConstructable first = Construction(
      world,
      world.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    ExRightClickConstructable second = Construction(
      world,
      world.ClientApi,
      new BlockPos(1, 0, 0, 0)
    );
    world.ClientApi.World.ClearReceivedCalls();

    Hint(first);
    Hint(second);

    Assert.Equal(2, Scans(world.ClientApi.World));
  }

  // Fails when the memo ignores the stage or the stored variants.
  [Fact]
  public void A_loaded_stage_and_variant_rebuild_the_hint() {
    TestWorld world = MetalWorld();
    ExRightClickConstructable behavior = Construction(
      world,
      world.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    world.ClientApi.World.ClearReceivedCalls();
    Hint(behavior);

    var rcc = (ExRightClickConstruction)
      ReflectionHelpers.GetField(behavior, "rcc")!;
    LoadStage(rcc, "steel");
    WorldInteraction[]? hint = Hint(behavior);

    Assert.NotNull(hint);
    Assert.Single(hint);
    Assert.Equal(new[] { "game:rod-steel 1" }, Listed(hint[0]));
    Assert.Equal(3, Scans(world.ClientApi.World));
  }

  // Fails when the memo ignores the next stage index while the stored variants are unchanged.
  [Fact]
  public void A_completed_stage_rebuilds_the_hint() {
    TestWorld world = new();
    world.RegisterItem("game:widget-a");
    world.RegisterItem("game:widget-b");
    ExRightClickConstructable behavior = Construction(
      world,
      world.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    var rcc = (ExRightClickConstruction)
      ReflectionHelpers.GetField(behavior, "rcc")!;
    rcc.Stages = new[]
    {
      new ExConstructionStage(),
      new ExConstructionStage
      {
        RequireStacks = new[]
        {
          Widget("item", null, null, null, "game:widget-a"),
        },
      },
      new ExConstructionStage
      {
        RequireStacks = new[]
        {
          Widget("item", null, null, null, "game:widget-b"),
        },
      },
    };
    rcc.CurrentCompletedStage = 0;
    Assert.Equal(new[] { "game:widget-a 1" }, Listed(Hint(behavior)![0]));

    rcc.CurrentCompletedStage = 1;

    Assert.Equal(new[] { "game:widget-b 1" }, Listed(Hint(behavior)![0]));
  }

  // Fails when the memo ignores the stored variants while the stage is unchanged.
  [Fact]
  public void A_changed_stored_variant_rebuilds_the_hint() {
    TestWorld world = MetalWorld();
    ExRightClickConstructable behavior = Construction(
      world,
      world.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    var rcc = (ExRightClickConstruction)
      ReflectionHelpers.GetField(behavior, "rcc")!;
    LoadStage(rcc, "iron");
    Assert.Equal(new[] { "game:rod-iron 1" }, Listed(Hint(behavior)![0]));

    LoadStage(rcc, "steel");

    Assert.Equal(new[] { "game:rod-steel 1" }, Listed(Hint(behavior)![0]));
  }

  private static void LoadStage(ExRightClickConstruction rcc, string metal) {
    var tree = new TreeAttribute();
    tree.SetInt("currentStage", 1);
    var wildcards = new TreeAttribute();
    wildcards["metal"] = new StringAttribute(metal);
    tree["wildcards"] = wildcards;
    rcc.FromTreeAttributes(tree);
  }

  // Fails when the cache is process-wide, keyed by the ingredient alone.
  [Fact]
  public void Each_world_scans_and_lists_its_own_items() {
    TestWorld first = MetalWorld();
    TestWorld second = MetalWorld();
    ExRightClickConstructable inFirst = Construction(
      first,
      first.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    ExRightClickConstructable inSecond = Construction(
      second,
      second.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    first.ClientApi.World.ClearReceivedCalls();
    second.ClientApi.World.ClearReceivedCalls();

    Hint(inFirst);
    Hint(inFirst);
    WorldInteraction[]? hint = Hint(inSecond);
    Hint(inSecond);

    Assert.Equal(2, Scans(first.ClientApi.World));
    Assert.Equal(2, Scans(second.ClientApi.World));
    Assert.Same(
      second.GetItem(new AssetLocation("game:metalplate-iron")),
      hint!
        [0]
        .Itemstacks.First(s => s.Collectible.Code.Path == "metalplate-iron")
        .Collectible
    );
  }

  private static ExConstructionIngredient Widget(
    string type,
    string[]? allowed,
    string[]? skip,
    string? attributes,
    string code = "game:widget-*"
  ) =>
    new() {
      Type = type == "block" ? EnumItemClass.Block : EnumItemClass.Item,
      Code = new AssetLocation(code),
      AllowedVariants = allowed,
      SkipVariants = skip,
      Attributes =
        attributes == null ? null : new JsonObject(JToken.Parse(attributes)),
    };

  // Fails when the cache key omits the ingredient field the case varies: Type, AllowedVariants,
  // SkipVariants or Attributes.
  [Theory]
  [InlineData("type")]
  [InlineData("allowed")]
  [InlineData("skip")]
  [InlineData("attributes")]
  public void Ingredients_differing_in_one_field_scan_and_list_separately(
    string field
  ) {
    TestWorld world = new();
    world.RegisterItem("game:widget-a").VariantStrict["variant"] = "a";
    world.RegisterItem("game:widget-b").VariantStrict["variant"] = "b";
    world.Register(TestBlocks.Configure(new Block(), "game:widget-c", 5100));

    (
      ExConstructionIngredient left,
      ExConstructionIngredient right,
      string[] listedLeft,
      string[] listedRight
    ) = field switch {
      "type" => (
        Widget("item", null, null, null),
        Widget("block", null, null, null),
        new[] { "game:widget-a 1", "game:widget-b 1" },
        new[] { "game:widget-c 1" }
      ),
      "allowed" => (
        Widget("item", new[] { "a" }, null, null),
        Widget("item", new[] { "b" }, null, null),
        new[] { "game:widget-a 1" },
        new[] { "game:widget-b 1" }
      ),
      "skip" => (
        Widget("item", null, new[] { "a" }, null),
        Widget("item", null, new[] { "b" }, null),
        new[] { "game:widget-b 1" },
        new[] { "game:widget-a 1" }
      ),
      _ => (
        Widget("item", null, null, "{\"flag\":true}", "game:widget-a"),
        Widget("item", null, null, null, "game:widget-a"),
        Array.Empty<string>(),
        new[] { "game:widget-a 1" }
      ),
    };
    ExRightClickConstructable first = Construction(
      world,
      world.ClientApi,
      new BlockPos(0, 0, 0, 0)
    );
    ExRightClickConstructable second = Construction(
      world,
      world.ClientApi,
      new BlockPos(1, 0, 0, 0)
    );
    Stage(first, left);
    Stage(second, right);
    world.ClientApi.World.ClearReceivedCalls();

    WorldInteraction[]? leftHint = Hint(first);
    WorldInteraction[]? rightHint = Hint(second);

    Assert.Equal(listedLeft, Listed(leftHint![0]));
    Assert.Equal(listedRight, Listed(rightHint![0]));
    Assert.Equal(2, Scans(world.ClientApi.World));
  }

  private static void Stage(
    ExRightClickConstructable behavior,
    ExConstructionIngredient ingredient
  ) {
    var rcc = (ExRightClickConstruction)
      ReflectionHelpers.GetField(behavior, "rcc")!;
    rcc.Stages = new[]
    {
      new ExConstructionStage(),
      new ExConstructionStage { RequireStacks = new[] { ingredient } },
    };
    rcc.CurrentCompletedStage = 0;
  }
}
