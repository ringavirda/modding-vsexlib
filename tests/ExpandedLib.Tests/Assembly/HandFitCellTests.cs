using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A hand fit aimed at a filler cell of a megablock whose principal implements <see cref="IHandFitCellTarget"/>: what the aim hands the target, what the ghost and the server take from it, and what a take-off returns.</summary>
public class HandFitCellTests {
  private static readonly BlockPos Principal = new(64, 16, 64, 0);
  private static readonly BlockPos Filler = new(65, 16, 64, 0);

  private const string Spec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":1,"perFit":2}}""";

  #region Aim

  // Fails when a filler cell resolves to the filler block, or the target is handed the principal's cell instead of the cell clicked or loses the hit.
  [Fact]
  public void Fit_OnFillerReachesPrincipal() {
    var scene = CellScene.Standing();
    var hit = new Vec3d(0.25, 0.5, 0.75);

    HandFitAim.Cell? aim = HandFitAim.OfCell(
      scene.World.Accessor,
      Filler,
      hit,
      scene.Player.ActiveSlot.Itemstack!,
      out string? why
    );

    Assert.Null(why);
    Assert.NotNull(aim);
    Assert.Same(scene.Post, aim.Target);
    Assert.Equal(Principal, aim.Principal);
    Assert.Equal(Filler, aim.At.Cell);
    Assert.Equal(hit, aim.At.Hit);
    Assert.Equal(Filler, scene.Post.PlannedAt?.Cell);
  }

  // Fails when the ghost falls back to the default mesh although the target answers its own.
  [Fact]
  public void Fit_GhostFromTarget() {
    var scene = CellScene.Standing();
    HandFitAim.Cell aim = HandFitAim.OfCell(
      scene.World.Accessor,
      Filler,
      null,
      scene.Player.ActiveSlot.Itemstack!,
      out _
    )!;

    IReadOnlyList<HandFitMesh>? meshes = HandAssemblyFitGhost.Of(
      Substitute.For<ICoreClientAPI>(),
      aim,
      scene.Player.ActiveSlot.Itemstack!
    );

    Assert.Same(scene.Post.Meshes, meshes);
  }

  #endregion

  #region Server

  // Fails when the companions the plan names are not taken from the inventory with the fit.
  [Fact]
  public void Fit_TakesCompanionsFromInventory() {
    var scene = CellScene.Standing();

    Assert.Null(scene.Fit());

    Assert.Equal(1, scene.Carried(scene.Washer));
    Assert.Equal(8, scene.Carried(scene.Bolt));
    Assert.Equal(2, scene.Player.ActiveSlot.StackSize);
    Assert.True(scene.Post.Fitted);
  }

  // Fails when a companion that is the held item's own code counts the held one in its count: a stack of two fits as a pair, a stack of one is refused.
  [Fact]
  public void Fit_CompanionMayBeTheHeldItem() {
    var scene = CellScene.Standing(washers: 0);
    scene.Post.CompanionCode = "test:shaft";
    scene.Post.CompanionCount = 1;
    scene.Player.Hold(new ItemStack(scene.Shaft, 2));

    Assert.Null(scene.Fit());

    Assert.Equal(0, scene.Carried(scene.Shaft));
    Assert.True(scene.Post.Fitted);

    var single = CellScene.Standing(washers: 0);
    single.Post.CompanionCode = "test:shaft";
    single.Post.CompanionCount = 1;
    single.Player.Hold(new ItemStack(single.Shaft, 1));

    Assert.Equal(HandAssemblyServer.NeedsCompanion, single.Fit()!.Code);
    Assert.Equal(1, single.Carried(single.Shaft));
  }

  // Fails when a missing companion is not refused by name, or anything is taken despite the refusal.
  [Fact]
  public void Fit_RefusedWhenACompanionIsMissing() {
    var scene = CellScene.Standing(washers: 1);

    AssemblyResult? refusal = scene.Fit();

    Assert.Equal(HandAssemblyServer.NeedsCompanion, refusal!.Code);
    Assert.Equal("2", refusal.Args[0]);
    Assert.Equal(1, scene.Carried(scene.Washer));
    Assert.Equal(10, scene.Carried(scene.Bolt));
    Assert.Equal(3, scene.Player.ActiveSlot.StackSize);
    Assert.Equal(0, scene.Wear);
    Assert.False(scene.Post.Fitted);
  }

  #endregion

  #region Take-off

  // Fails when a take-off of the fitted part leaves out the fasteners the fit spent.
  [Fact]
  public void FitTakeOff_ReturnsPartAndFasteners() {
    var scene = CellScene.Fitted();

    Assert.Null(scene.TakeOff());

    Assert.Equal(10, scene.Carried(scene.Bolt));
    Assert.Equal(3, scene.Carried(scene.Shaft));
    Assert.False(scene.Post.Fitted);
  }

  // Fails when the companions the fit took are not returned with the part.
  [Fact]
  public void FitTakeOff_ReturnsCompanions() {
    var scene = CellScene.Fitted();

    Assert.Null(scene.TakeOff());

    Assert.Equal(3, scene.Carried(scene.Washer));
  }

  // Fails when the take-off asks the fit's own tool: the target names the wrench for the take-off and the fit's tool is a hammer, so the hammer in the main hand is refused and the wrench takes the part off.
  [Fact]
  public void FitTakeOff_UsesTheTargetsTool() {
    var scene = CellScene.Fitted();
    scene.Post.RemovalTool = "test:wrench-*";
    scene.SpecTool("test:hammer-*");

    Assert.Equal(
      HandAssemblyServer.NeedsToolMain,
      scene.TakeOff(withTool: true, tool: "test:hammer-iron")!.Code
    );
    Assert.True(scene.Post.Fitted);
    Assert.Null(scene.TakeOff());
    Assert.False(scene.Post.Fitted);
  }

  // Fails when the tool in the main hand is not required: a take-off with the tool elsewhere changes nothing.
  [Fact]
  public void FitTakeOff_RefusedWithoutTool() {
    var scene = CellScene.Fitted();

    Assert.Equal(
      HandAssemblyServer.NeedsToolMain,
      scene.TakeOff(withTool: false)!.Code
    );

    Assert.True(scene.Post.Fitted);
    Assert.Equal(8, scene.Carried(scene.Bolt));
  }

  #endregion

  private sealed class CellScene {
    private CellScene(int washers) {
      World = new TestWorld();
      Player = World.Player();
      Player.StandAt(Principal, BlockFacing.SOUTH);
      Player.GameMode = EnumGameMode.Survival;
      Wrench = new WearCounter {
        Code = new AssetLocation("test:wrench-iron"),
        ItemId = 900,
        Durability = 100,
        StorageFlags =
          EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand,
      };
      World.Register(Wrench);
      Bolt = World.RegisterItem("test:bolt");
      Shaft = World.RegisterItem("test:shaft");
      Washer = World.RegisterItem("test:washer");
      Player.Hold(new ItemStack(Shaft, 3));
      Player.Hotbar[1].Itemstack = new ItemStack(Bolt, 10);
      Player.Hotbar[2].Itemstack =
        washers > 0 ? new ItemStack(Washer, washers) : null;
      Player.HoldOffHand(new ItemStack(Wrench));
      Player.Player.InventoryManager.Inventories["backpack-test"] =
        TestInventory.Of(World, 4, "backpack-test");
      Post = new CellPost(Shaft, Washer) {
        Attributes = new JsonObject(JToken.Parse(Spec)),
      };
      TestBlocks.Configure(Post, "test:post", 400);
      World.Register(Post);
      World.Place(
        Principal.AddCopy(BlockFacing.DOWN),
        TestBlocks.Configure(new Block(), "game:rock", 5)
      );
      World.Place(Principal, Post);
      World.PlaceFiller(Filler, null, Principal);
    }

    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public Item Wrench { get; }
    public int Wear => ((WearCounter)Wrench).Uses;
    public Item Bolt { get; }
    public Item Shaft { get; }
    public Item Washer { get; }
    public CellPost Post { get; }

    /// <summary>Gives the principal's block another fit tool.</summary>
    public void SpecTool(string tool) =>
      Post.Attributes = new JsonObject(
        JToken.Parse(Spec.Replace("test:wrench-*", tool))
      );

    public static CellScene Standing(int washers = 3) => new(washers);

    /// <summary>A scene after a fit on the filler: shaft 2 held, 8 bolts, 1 washer carried, the target holding the part.</summary>
    public static CellScene Fitted() {
      var scene = new CellScene(3);
      Assert.Null(scene.Fit());
      return scene;
    }

    public int Carried(Item item) =>
      ExCarried.Count(Player.Player, item.Code.ToString());

    public AssemblyResult? Fit() =>
      HandAssemblyServer.Fit(
        World.World,
        Player.Player,
        new AssemblyFit {
          X = Filler.X,
          Y = Filler.Y,
          Z = Filler.Z,
          Dimension = Filler.dimension,
          Slot = Player.ActiveNumber,
          ItemCode =
            Player.ActiveSlot.Itemstack?.Collectible.Code.ToString() ?? "",
          HitX = 0.5,
          HitY = 0.5,
          HitZ = 0.5,
        }
      );

    /// <summary>Takes the fitted part off sneaking, with the tool in the main hand when <paramref name="withTool"/> is set and the shaft otherwise.</summary>
    public AssemblyResult? TakeOff(bool withTool = true, string? tool = null) {
      Player.Sneaking = true;
      Player.Hotbar[3].Itemstack = new ItemStack(
        tool == null ? Wrench : World.RegisterItem(tool)
      );
      Player.Select(withTool ? 3 : 0);
      return HandAssemblyServer.TakeOff(
        World.World,
        Player.Player,
        new AssemblyTakeOff {
          X = Filler.X,
          Y = Filler.Y,
          Z = Filler.Z,
          Dimension = Filler.dimension,
          HitX = 0.5,
          HitY = 0.5,
          HitZ = 0.5,
        }
      );
    }
  }

  private sealed class CellPost(Item shaft, Item washer)
    : Block,
      IHandFitCellTarget {
    public bool Fitted;
    public string CompanionCode = "test:washer";
    public int CompanionCount = 2;
    public string? RemovalTool;
    public HandFitPoint? PlannedAt;
    public readonly IReadOnlyList<HandFitMesh> Meshes =
    [
      new HandFitMesh(new MeshData(), new Vec3d(1, 0, 0)),
    ];

    public HandFitPlan? Plan(
      IBlockAccessor accessor,
      BlockPos principal,
      HandFitPoint at,
      ItemStack held,
      out string? failureCode
    ) {
      PlannedAt = at;
      failureCode = null;
      return new HandFitPlan([new HandFitCompanion(CompanionCode, CompanionCount)], 2);
    }

    public IReadOnlyList<HandFitMesh>? Ghost(
      ICoreClientAPI capi,
      BlockPos principal,
      HandFitPoint at,
      ItemStack held,
      HandFitPlan plan
    ) => Meshes;

    public bool Fit(
      IWorldAccessor world,
      BlockPos principal,
      HandFitPoint at,
      IPlayer player,
      ItemSlot held,
      ref string failureCode
    ) {
      Fitted = true;
      held.TakeOut(1);
      held.MarkDirty();
      return true;
    }

    public HandFitRemoval? FittedAt(
      IBlockAccessor accessor,
      BlockPos principal,
      HandFitPoint at
    ) =>
      Fitted
        ? new HandFitRemoval(
            [new ItemStack(shaft, 1), new ItemStack(washer, 2)],
            2,
            RemovalTool
          )
        : null;

    public bool TakeOff(
      IWorldAccessor world,
      BlockPos principal,
      HandFitPoint at,
      IPlayer player
    ) {
      Fitted = false;
      return true;
    }
  }

  private sealed class WearCounter : Item {
    public int Uses;

    public override void DamageItem(
      IWorldAccessor world,
      Entity byEntity,
      ItemSlot itemslot,
      int amount = 1,
      bool destroyOnZeroDurability = true
    ) => Uses += amount;
  }
}
