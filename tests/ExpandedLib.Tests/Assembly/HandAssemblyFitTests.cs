using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>An item fitted onto a placed block that implements <see cref="IHandFitTarget"/>, from the main hand with the tool in the off hand: what the server checks and in what order, what it spends and records, and what the aim skips.</summary>
public class HandAssemblyFitTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);

  private const string FitSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":1,"perFit":2}}""";

  private const string NoCostSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":1}}""";

  #region Server

  // Fails when the fit spends no fasteners, spends another count than the def's perFit, or does not wear the tool.
  [Fact]
  public void Fit_SpendsPerFitAndRecordsIt() {
    var scene = FitScene.Standing();

    Assert.Null(scene.Fit());

    Assert.Equal(8, scene.Carried(scene.Bolt));
    Assert.Equal(2, scene.Record!.Joint!.StackSize);
    Assert.Equal(1, scene.Wear);
    Assert.Equal("test:done", scene.World.GetBlock(Cell).Code.ToString());
  }

  // Fails when the distance to the piece is not checked.
  [Fact]
  public void Fit_RefusedOutOfRange() {
    var scene = FitScene.Standing();
    scene.Player.Entity.Pos.SetPos(Cell.X + 40, Cell.Y, Cell.Z);

    Assert.Equal(HandAssemblyServer.OutOfRange, scene.Fit()!.Code);

    scene.AssertNothingChanged();
  }

  // Fails when the land claim is not asked.
  [Fact]
  public void Fit_RefusedOnClaimedLand() {
    var scene = FitScene.Standing();
    scene
      .World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(false);

    Assert.Equal(HandAssemblyServer.Claimed, scene.Fit()!.Code);

    scene.AssertNothingChanged();
  }

  // Fails when the tool in the off hand is not required: a tool elsewhere in the hotbar fits nothing.
  [Fact]
  public void Fit_RefusedWithoutTool() {
    var scene = FitScene.Standing();
    scene.Player.HoldOffHand(null);
    scene.Player.Hotbar[2].Itemstack = new ItemStack(scene.Wrench);

    Assert.Equal(HandAssemblyServer.NeedsTool, scene.Fit()!.Code);

    scene.AssertNothingChanged();
  }

  // Fails when the fasteners the fit needs are not counted before the swap.
  [Fact]
  public void Fit_RefusedWithTooFewFasteners() {
    var scene = FitScene.Standing(bolts: 1);

    Assert.Equal(HandAssemblyServer.NeedsFasteners, scene.Fit()!.Code);

    Assert.Equal(1, scene.Carried(scene.Bolt));
    Assert.Equal(0, scene.Wear);
    Assert.Equal("test:post", scene.World.GetBlock(Cell).Code.ToString());
  }

  // Fails when the fasteners are spent before the target is asked, or the target's refusal is not the one sent.
  [Fact]
  public void Fit_TargetRefusalSpendsNothing() {
    var scene = FitScene.Standing(refuse: "exlib-test-jammed");

    Assert.Equal("exlib-test-jammed", scene.Fit()!.Code);

    scene.AssertNothingChanged();
  }

  // Fails when the tool check is skipped for a def whose fit costs no fasteners.
  [Fact]
  public void Fit_PerFitZeroStillNeedsTool() {
    var scene = FitScene.Standing(NoCostSpec);
    scene.Player.HoldOffHand(null);

    Assert.Equal(HandAssemblyServer.NeedsTool, scene.Fit()!.Code);

    Assert.Equal("test:post", scene.World.GetBlock(Cell).Code.ToString());
  }

  // Fails when the fit's spend is left out of the stacks a take-off returns.
  [Fact]
  public void Fit_TakeOffReturnsFitFasteners() {
    var scene = FitScene.Standing();
    Assert.Null(scene.Fit());

    var (stacks, _) = HandAssemblyServer.TakeOffReturns(
      scene.World.Accessor,
      Cell
    );

    Assert.Equal(
      2,
      stacks.Where(s => s.Collectible == scene.Bolt).Sum(s => s.StackSize)
    );
  }

  // Fails when creative spends the fasteners, wears the tool, asks for it, or records a spend.
  [Fact]
  public void Fit_CreativeSpendsNothing() {
    var scene = FitScene.Standing(bolts: 0);
    scene.Player.GameMode = EnumGameMode.Creative;
    scene.Player.HoldOffHand(null);

    Assert.Null(scene.Fit());

    Assert.Equal("test:done", scene.World.GetBlock(Cell).Code.ToString());
    Assert.Null(scene.Record!.Joint);
    Assert.Equal(0, scene.Wear);
  }

  #endregion

  #region Aim

  // Fails when the replaceable check is dropped: an air or plant cell whose block implements the target is aimed at.
  [Fact]
  public void HandFitAim_SkipsReplaceableCell() {
    var scene = FitScene.Standing();
    scene.Post.Replaceable = 6000;

    Assert.Null(
      HandFitAim.Of(
        scene.World.Accessor,
        Cell,
        scene.Player.ActiveSlot.Itemstack,
        out _
      )
    );
  }

  #endregion

  private sealed class FitScene {
    private FitScene(TestWorld world, string spec, int bolts, string? refuse) {
      World = world;
      Player = world.Player();
      Player.StandAt(Cell, BlockFacing.SOUTH);
      Player.GameMode = EnumGameMode.Survival;
      Wrench = new WearCounter {
        Code = new AssetLocation("test:wrench-iron"),
        ItemId = 900,
        Durability = 100,
        StorageFlags =
          EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand,
      };
      world.Register(Wrench);
      Bolt = world.RegisterItem("test:bolt");
      Shaft = world.RegisterItem("test:shaft");
      Player.Hold(new ItemStack(Shaft, 3));
      Player.Hotbar[1].Itemstack =
        bolts > 0 ? new ItemStack(Bolt, bolts) : null;
      Player.HoldOffHand(new ItemStack(Wrench));
      Player.Player.InventoryManager.Inventories["backpack-test"] =
        TestInventory.Of(world, 4, "backpack-test");
      Done = TestBlocks.Configure(new Block(), "test:done", 401);
      world.Register(Done);
      Post = new PostBlock(world, Done, refuse) {
        Attributes = new JsonObject(JToken.Parse(spec)),
      };
      TestBlocks.Configure(Post, "test:post", 400);
      world.Register(Post);
    }

    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public Item Wrench { get; }
    public int Wear => ((WearCounter)Wrench).Uses;
    public Item Bolt { get; }
    public Item Shaft { get; }
    public Block Done { get; }
    public Block Post { get; }
    public BEBehaviorAssembled? Record =>
      BEBehaviorAssembled.At(World.Accessor, Cell);

    public static FitScene Standing(
      string spec = FitSpec,
      int bolts = 10,
      string? refuse = null
    ) {
      var world = new TestWorld();
      world.Place(
        Cell.AddCopy(BlockFacing.DOWN),
        TestBlocks.Configure(new Block(), "game:rock", 5)
      );
      var scene = new FitScene(world, spec, bolts, refuse);
      world.Place(Cell, scene.Post);
      return scene;
    }

    public int Carried(Item item) =>
      ExCarried.Count(Player.Player, item.Code.ToString());

    public AssemblyResult? Fit() =>
      HandAssemblyServer.Fit(
        World.World,
        Player.Player,
        new AssemblyFit {
          X = Cell.X,
          Y = Cell.Y,
          Z = Cell.Z,
          Dimension = Cell.dimension,
          Slot = Player.ActiveNumber,
          ItemCode =
            Player.ActiveSlot.Itemstack?.Collectible.Code.ToString() ?? "",
        }
      );

    public void AssertNothingChanged() {
      Assert.Equal(3, Player.ActiveSlot.StackSize);
      Assert.Equal(10, Carried(Bolt));
      Assert.Equal(0, Wear);
      Assert.Equal("test:post", World.GetBlock(Cell).Code.ToString());
    }
  }

  private sealed class PostBlock(TestWorld world, Block done, string? refuse)
    : Block,
      IHandFitTarget {
    public Block? FitResult(
      IBlockAccessor accessor,
      BlockPos pos,
      ItemStack held,
      out string? failureCode
    ) {
      failureCode = null;
      return done;
    }

    public bool Fit(
      IWorldAccessor w,
      BlockPos pos,
      IPlayer player,
      ItemSlot held,
      ref string failureCode
    ) {
      if (refuse != null) {
        failureCode = refuse;
        return false;
      }
      var entity = new BlockEntityAssembled();
      entity.Behaviors.Add(new BEBehaviorAssembled(entity));
      world.Place(pos, done, entity);
      world.Initialize(entity);
      held.TakeOut(1);
      held.MarkDirty();
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
