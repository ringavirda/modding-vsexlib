using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
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

namespace ExpandedLib.Tests;

/// <summary>A cap fitted to or taken off an open crossing of a fixed piece by the wrench click: what it checks, what it takes and returns, and what a fitted cap does to the piece's networks, break and take-off.</summary>
public class HandAssemblyCapTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly string[] Axis = ["ns", "we", "ud"];

  private const string CastSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":4,"cap":"test:cap-cast"}}""";

  private const string PlatedSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt-bundle","perJoint":2,"cap":"test:cap-plated"}}""";

  private const string NoCapSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":4}}""";

  private static Vec3d Hit(BlockFacing face) =>
    new(
      0.5 + 0.5 * face.Normali.X,
      0.5 + 0.5 * face.Normali.Y,
      0.5 + 0.5 * face.Normali.Z
    );

  #region Fitting

  // Fails when the cap fitted is not taken from the off hand, or the fasteners of the def's joint are not spent.
  [Fact]
  public void Cap_FitSpendsCapAndJointFasteners() {
    var scene = CapScene.Standing(bolts: 10);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(1, scene.Player.OffHandSlot.StackSize);
    Assert.Equal(6, scene.Carried(scene.Bolt));
    Assert.Equal(
      "test:cap-cast",
      scene.Record.CapAt(BlockFacing.NORTH)!.Collectible.Code.ToString()
    );
    Assert.Equal(4, scene.Record.CapFastenersAt(BlockFacing.NORTH)!.StackSize);
    Assert.Null(scene.Record.CapAt(BlockFacing.SOUTH));
  }

  // Fails when the click takes the first arm it finds and not the crossing nearest the hit point.
  [Fact]
  public void Cap_FitsTheCrossingNearestTheHit() {
    var scene = CapScene.Standing();

    Assert.Null(scene.Click(BlockFacing.SOUTH));

    Assert.NotNull(scene.Record.CapAt(BlockFacing.SOUTH));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when air beyond an end is read as closing it.
  [Fact]
  public void Cap_FitsOnEndFacingAir() {
    var scene = CapScene.Standing();

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when the fasteners are spent as one, not as the def's perJoint, or the def's own fastener is not the one spent.
  [Fact]
  public void Cap_PlatedFitSpendsTwoBoltBundles() {
    var scene = CapScene.Standing(PlatedSpec, offHand: "plated");
    scene.Give(scene.Bundle, 5);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(3, scene.Carried(scene.Bundle));
    Assert.Equal(2, scene.Record.CapFastenersAt(BlockFacing.NORTH)!.StackSize);
    Assert.Equal(
      "test:cap-plated",
      scene.Record.CapAt(BlockFacing.NORTH)!.Collectible.Code.ToString()
    );
  }

  // Fails when a crossing beside an occupied cell that does not shut it is refused: a pipe whose joint was refused, a structure filler, a block solid only on its top.
  [Theory]
  [InlineData("turned pipe")]
  [InlineData("filler host")]
  [InlineData("solid on top only")]
  public void Cap_FitsBesideOccupiedNotSolid(string beside) {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, beside);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when the closing-block check is dropped.
  [Fact]
  public void Cap_RefusedBesideSolidFace() {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, "full block");

    Assert.Equal(
      HandAssemblyServer.CapClosed,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when a face joined to the pipe beside it is capped.
  [Fact]
  public void Cap_RefusedOnJoinedFace() {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, "joined pipe");

    Assert.Equal(
      HandAssemblyServer.CapJoined,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when any cap the player holds fits the def: a plated cap is offered to a cast def.
  [Fact]
  public void Cap_RefusesAnotherSetsCap() {
    var scene = CapScene.Standing(offHand: "plated");

    AssemblyResult refused = scene.Click(BlockFacing.NORTH)!;

    Assert.Equal(HandAssemblyServer.NeedsTool, refused.Code);
    scene.AssertNothingTaken();
  }

  // Fails when the fasteners the joint needs are not counted before the cap is taken.
  [Fact]
  public void Cap_RefusedWithoutEnoughFasteners() {
    var scene = CapScene.Standing(bolts: 3);

    Assert.Equal(
      HandAssemblyServer.NeedsFasteners,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    Assert.Equal(2, scene.Player.OffHandSlot.StackSize);
    Assert.Equal(3, scene.Carried(scene.Bolt));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when the wrench in the main hand is not required.
  [Fact]
  public void Cap_RefusedWithoutTheWrenchInTheMainHand() {
    var scene = CapScene.Standing();
    scene.Player.Hold(null);

    Assert.Equal(
      HandAssemblyServer.NeedsToolMain,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when a piece that was never fixed takes a cap.
  [Fact]
  public void Cap_RefusedOnAPieceThatIsNotFixed() {
    var scene = CapScene.Standing(fixedPiece: false);

    Assert.Equal(
      HandAssemblyServer.Fixed,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when the player's distance to the piece is not checked.
  [Fact]
  public void Cap_RefusedBeyondThePickingRange() {
    var scene = CapScene.Standing();
    scene.Player.Entity.Pos.SetPos(Cell.X + 40, Cell.Y, Cell.Z);

    Assert.Equal(
      HandAssemblyServer.OutOfRange,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when the land claim is not asked.
  [Fact]
  public void Cap_RefusedInAClaimedCell() {
    var scene = CapScene.Standing();
    scene
      .World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(false);

    Assert.Equal(
      HandAssemblyServer.Claimed,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when an empty off hand is read as a cap offered, so the click is refused for want of a tool.
  [Fact]
  public void Cap_ClickWithNoCapInTheOffHandOffersNothing() {
    var scene = CapScene.Standing(offHand: null);

    Assert.Equal(
      HandAssemblyServer.Fixed,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when a def that declares no cap takes the cast cap the player holds.
  [Fact]
  public void Cap_DefWithoutACapOffersNothing() {
    var scene = CapScene.Standing(NoCapSpec);

    Assert.Equal(
      HandAssemblyServer.Fixed,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when creative spends the cap or the fasteners, or asks for the wrench.
  [Fact]
  public void Cap_CreativeFitsWithoutSpendingOrTheWrench() {
    var scene = CapScene.Standing(bolts: 0);
    scene.Player.GameMode = EnumGameMode.Creative;
    scene.Player.Hold(null);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(2, scene.Player.OffHandSlot.StackSize);
    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Null(scene.Record.CapFastenersAt(BlockFacing.NORTH));
  }

  // Fails when the wrench click through Rotate does not reach the cap, or the refusal is not told to the player.
  [Fact]
  public void Cap_TheWrenchClickFitsAndTellsARefusal() {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.SOUTH, "full block");

    scene.Rotate(BlockFacing.NORTH);
    scene.Rotate(BlockFacing.SOUTH);

    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Null(scene.Record.CapAt(BlockFacing.SOUTH));
    Assert.Contains(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call =>
        call.GetMethodInfo().Name == "SendIngameError"
        && (string?)call.GetArguments()[0] == HandAssemblyServer.CapClosed
    );
  }

  // Fails when a sneaking click fits a cap: sneak with the wrench is the take-off.
  [Fact]
  public void Cap_SneakingClickFitsNothing() {
    var scene = CapScene.Standing();
    scene.Player.Sneaking = true;

    scene.Rotate(BlockFacing.NORTH);

    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  #endregion

  #region Taking off

  // Fails when taking a cap off does not give the cap back.
  [Fact]
  public void Cap_TakeOffReturnsCapAndFasteners() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    Assert.Equal(1, scene.Carried(scene.CastCap));

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(10, scene.Carried(scene.Bolt));
    Assert.Equal(2, scene.Carried(scene.CastCap));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Null(scene.Record.CapFastenersAt(BlockFacing.NORTH));
  }

  // Fails when the fasteners a cap carried are not given back.
  [Fact]
  public void Cap_TakeOffReturnsTheFastenersItCarried() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    Assert.Equal(6, scene.Carried(scene.Bolt));

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(10, scene.Carried(scene.Bolt));
  }

  // Fails when the fit does not mark the cell for redraw, so a client draws no cap until the chunk is sent again.
  [Fact]
  public void Cap_FitMarksTheCellForRedraw() {
    var scene = CapScene.Standing();
    scene.World.TakeRedrawMark(Cell);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.True(scene.World.TakeRedrawMark(Cell));
  }

  // Fails when the take-off does not mark the cell for redraw, so a client keeps drawing the cap.
  [Fact]
  public void Cap_TakeOffMarksTheCellForRedraw() {
    var scene = CapScene.Standing();
    Assert.Null(scene.Click(BlockFacing.NORTH));
    scene.World.TakeRedrawMark(Cell);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.True(scene.World.TakeRedrawMark(Cell));
  }

  // Fails when the fit leaves the piece in the network built before the cap, not in one built afresh.
  [Fact]
  public void Cap_FitRebuildsTheNetwork() {
    var scene = CapScene.Lumped(bolts: 10);
    BlockNetwork? before = scene.World.NetworkAt(Cell);

    Assert.Null(scene.Click(BlockFacing.UP));

    BlockNetwork? after = scene.World.NetworkAt(Cell);
    Assert.NotNull(after);
    Assert.NotSame(before, after);
    Assert.Contains(Cell, after.Nodes);
  }

  // Fails when a take-off needs the cap in the off hand, or is refused to a player with a full pack instead of dropping at the feet.
  [Fact]
  public void Cap_TakeOffWithAFullPackDropsAtTheFeet() {
    var scene = CapScene.Standing(bolts: 4);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    scene.Player.HoldOffHand(null);
    scene.FillPack();

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Contains(scene.World.Drops, drop => drop.Item == scene.CastCap);
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when creative takes the cap and fasteners back.
  [Fact]
  public void Cap_CreativeTakeOffReturnsNothing() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    scene.Player.GameMode = EnumGameMode.Creative;

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(6, scene.Carried(scene.Bolt));
    Assert.Equal(1, scene.Carried(scene.CastCap));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when taking the cap off a lumped pipe's end does not rebuild its network, so a pipe laid beside while it was capped stays apart.
  [Fact]
  public void Cap_TakeOffReopens() {
    var scene = CapScene.Lumped(bolts: 10);
    Assert.Equal(
      ExlibValues.LitresPerPipe - ExlibValues.GasLeakRate,
      scene.Ticked(),
      3
    );

    scene.Rotate(BlockFacing.UP);
    Assert.Equal(ExlibValues.LitresPerPipe, scene.Ticked(), 3);
    BlockPos above = scene.JoinAbove();
    Assert.NotSame(scene.World.NetworkAt(Cell), scene.World.NetworkAt(above));
    scene.Rotate(BlockFacing.UP);

    Assert.Same(scene.World.NetworkAt(Cell), scene.World.NetworkAt(above));
  }

  #endregion

  #region The scenario player's click

  // Fails when the live run wears the wrench only on a click the server took: a refusal wears it as well, and creative never does.
  [Theory]
  [InlineData("fit", 1)]
  [InlineData("refusal", 1)]
  [InlineData("creative", 0)]
  public void Cap_PlayWearsTheWrenchOnEveryClickOutsideCreative(
    string click,
    int wear
  ) {
    var scene = CapScene.Standing();
    if (click == "refusal")
      scene.Beside(BlockFacing.NORTH, "full block");
    if (click == "creative")
      scene.Player.GameMode = EnumGameMode.Creative;
    var ctx = new ScenarioContext(
      scene.World.World,
      new ScenarioWorld(
        scene.World.World,
        new BlockPos(0, 0, 0),
        scene.World.Networks
      ),
      () => 0,
      new ScenarioErrors([]),
      _ => { },
      "case.json"
    );

    AssemblyResult? refused = HandAssemblyPlay.Cap(
      ctx,
      scene.World.World,
      scene.Player.Player,
      Cell,
      BlockFacing.NORTH
    );

    Assert.Equal(
      click == "refusal" ? HandAssemblyServer.CapClosed : null,
      refused?.Code
    );
    Assert.Equal(wear, scene.Wear);
  }

  #endregion

  #region Breaking and taking the piece off

  // Fails when breaking the piece returns the fasteners the cap carried.
  [Fact]
  public void Cap_BreakLosesFasteners() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));

    scene.World.Accessor.BreakBlock(Cell, null);

    Assert.Equal(6, scene.Carried(scene.Bolt));
    Assert.All(
      scene.World.Drops,
      drop => Assert.NotSame(scene.Bolt, drop.Item)
    );
  }

  // Fails when the cap is not dropped with the piece.
  [Fact]
  public void Cap_BreakDropsTheCapWithThePiece() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));

    scene.World.Accessor.BreakBlock(Cell, null);

    Assert.Contains(scene.World.Drops, drop => drop.Item == scene.CastCap);
  }

  // Fails when a creative break drops the cap.
  [Fact]
  public void Cap_CreativeBreakDropsNoCap() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    scene.Player.GameMode = EnumGameMode.Creative;

    scene.World.Accessor.BreakBlock(Cell, scene.Player.Player);

    Assert.DoesNotContain(
      scene.World.Drops,
      drop => drop.Item == scene.CastCap
    );
  }

  // Fails when taking the whole piece off leaves its caps behind.
  [Fact]
  public void Cap_PieceTakeOffReturnsItsCaps() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    scene.Player.HoldOffHand(null);
    int before = scene.Carried(scene.CastCap);

    Assert.Null(scene.TakeOffPiece());

    Assert.Equal(before + 1, scene.Carried(scene.CastCap));
  }

  // Fails when taking the whole piece off keeps the fasteners its caps carried.
  [Fact]
  public void Cap_PieceTakeOffReturnsTheCapFasteners() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Click(BlockFacing.NORTH));
    Assert.Equal(6, scene.Carried(scene.Bolt));

    Assert.Null(scene.TakeOffPiece());

    Assert.Equal(10, scene.Carried(scene.Bolt));
  }

  #endregion

  private sealed class CapScene {
    private readonly TestNetworkBlock[] _family = [];
    private readonly LumpedNode? _lumped;
    private int _nextId = 200;

    private CapScene(TestWorld world, string spec, string? offHand, int bolts) {
      World = world;
      Player = world.Player();
      Player.StandAt(Cell, BlockFacing.SOUTH);
      Player.GameMode = EnumGameMode.Survival;
      Wrench = new WearCounter {
        Code = new AssetLocation("test:wrench-iron"),
        ItemId = 900,
        Durability = 100,
      };
      world.Register(Wrench);
      Bolt = world.RegisterItem("test:bolt");
      Bundle = world.RegisterItem("test:bolt-bundle");
      CastCap = world.RegisterItem("test:cap-cast");
      PlatedCap = world.RegisterItem("test:cap-plated");
      Player.Hold(new ItemStack(Wrench));
      Player.Hotbar[1].Itemstack =
        bolts > 0 ? new ItemStack(Bolt, bolts) : null;
      Player.HoldOffHand(
        offHand switch {
          "plated" => new ItemStack(PlatedCap, 2),
          null => null,
          _ => new ItemStack(CastCap, 2),
        }
      );
      Player.Player.InventoryManager.Inventories["backpack-test"] =
        TestInventory.Of(world, 4, "backpack-test");
      Spec = spec;
    }

    private string Spec { get; }
    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public Item Wrench { get; }
    public int Wear => ((WearCounter)Wrench).Uses;
    public Item Bolt { get; }
    public Item Bundle { get; }
    public Item CastCap { get; }
    public Item PlatedCap { get; }
    public BEBehaviorAssembled Record { get; private set; } = null!;

    public static CapScene Standing(
      string spec = CastSpec,
      string? offHand = "cast",
      int bolts = 10,
      bool fixedPiece = true
    ) {
      var world = new TestWorld();
      world.RegisterNetwork("test", sys => new StubNetwork(sys));
      world.Place(
        Cell.AddCopy(BlockFacing.DOWN),
        TestBlocks.Configure(new Block(), "game:rock", 5)
      );
      var scene = new CapScene(world, spec, offHand, bolts);
      TestNetworkBlock[] family = TestNetworkBlock.Family(
        "test",
        "test:node",
        "Axis",
        Axis
      );
      foreach (TestNetworkBlock block in family) {
        block.Attributes = new JsonObject(JToken.Parse(spec));
        world.Register(block);
      }
      var entity = new OrientableNode {
        Orientation = "ns",
        PossibleOrientations = Axis,
      };
      scene.Record = new BEBehaviorAssembled(entity) { Fixed = fixedPiece };
      entity.Behaviors.Add(scene.Record);
      world.Place(Cell, family.First(b => b.Orientation == "ns"), entity);
      world.Initialize(entity);
      return scene;
    }

    public static CapScene Lumped(int bolts) {
      var world = new TestWorld();
      world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
      var scene = new CapScene(world, CastSpec, "cast", bolts);
      var entity = new BlockEntityAssembled();
      scene.Record = new BEBehaviorAssembled(entity) { Fixed = true };
      entity.Behaviors.Add(scene.Record);
      LumpedNode node = TestBlocks.Configure(
        new LumpedNode(),
        "test:lumped",
        300
      );
      node.Attributes = new JsonObject(JToken.Parse(CastSpec));
      world.Place(Cell, node, entity);
      world.AddNode(Cell, "pipe");
      return scene;
    }

    public BlockPos JoinAbove() {
      BlockPos pos = Cell.AddCopy(BlockFacing.UP);
      var entity = new BlockEntityAssembled();
      LumpedNode node = TestBlocks.Configure(
        new LumpedNode(BlockFacing.DOWN),
        "test:lumped-above",
        301
      );
      World.Place(pos, node, entity);
      World.AddNode(pos, "pipe");
      return pos;
    }

    public int Carried(Item item) =>
      ExCarried.Count(Player.Player, item.Code.ToString());

    public void Give(Item item, int count) =>
      Assert.True(
        Player.Player.InventoryManager.TryGiveItemstack(
          new ItemStack(item, count),
          true
        )
      );

    public AssemblyResult? Click(BlockFacing face) {
      Player.Sneaking = false;
      return HandAssemblyServer.Cap(
        World.World,
        Player.Player,
        Cell,
        Hit(face)
      );
    }

    public void Rotate(BlockFacing face) =>
      ((BlockNetworkNode)World.GetBlock(Cell)).Rotate(
        Player.Entity,
        new BlockSelection {
          Position = Cell.Copy(),
          Face = BlockFacing.UP,
          HitPosition = Hit(face),
        },
        1
      );

    public AssemblyResult? TakeOffPiece() {
      Player.Hold(new ItemStack(Wrench));
      Player.Sneaking = true;
      return HandAssemblyServer.TakeOff(
        World.World,
        Player.Player,
        new AssemblyTakeOff {
          X = Cell.X,
          Y = Cell.Y,
          Z = Cell.Z,
        }
      );
    }

    public float Ticked() {
      var net = (PipeNetwork)World.NetworkAt(Cell)!;
      net.RestoreState(
        new PipeNetworkState {
          Volume = ExlibValues.LitresPerPipe,
          MediumType = "Steam",
        }
      );
      World.Tick();
      return net.State!.Volume;
    }

    public void FillPack() {
      Item stone = World.RegisterItem("test:stone");
      foreach (
        ItemSlot slot in Player
          .Player.InventoryManager.GetHotbarInventory()
          .Concat(Player.Player.InventoryManager.GetOwnInventory("backpack"))
      )
        if (slot.Empty)
          slot.Itemstack = new ItemStack(stone);
    }

    public void AssertNothingTaken() {
      Assert.Equal(2, Player.OffHandSlot.StackSize);
      Assert.Equal(10, Carried(Bolt));
      Assert.Null(Record.CapAt(BlockFacing.NORTH));
      Assert.Null(Record.CapFastenersAt(BlockFacing.NORTH));
      Assert.Equal(
        100,
        Wrench.GetRemainingDurability(
          Player.ActiveSlot.Itemstack ?? new ItemStack(Wrench)
        )
      );
    }

    public void Beside(BlockFacing face, string kind) {
      BlockPos pos = Cell.AddCopy(face);
      switch (kind) {
        case "joined pipe":
          World.Place(pos, TestNetworkBlock.Create("test", "ns", _nextId++));
          World.AddNode(pos, "test");
          break;
        case "turned pipe":
          TestNetworkBlock turned = TestNetworkBlock.Create(
            "test",
            "we",
            _nextId++
          );
          turned.SideSolid = new SmallBoolArray(0);
          World.Place(pos, turned);
          World.AddNode(pos, "test");
          break;
        case "filler host":
          World.Place(pos, Solid(new FillerHost(), 0x3F));
          break;
        case "solid on top only":
          World.Place(pos, Solid(new Block(), 1 << BlockFacing.UP.Index));
          break;
        default:
          World.Place(pos, Solid(new Block(), 0x3F));
          break;
      }
    }

    private Block Solid(Block block, int sides) {
      TestBlocks.Configure(block, $"test:solid-{_nextId}", _nextId++);
      block.SideSolid = new SmallBoolArray(sides);
      return block;
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

  private sealed class FillerHost : Block, IFillerHost {
    public JsonObject? FillerOffsets => null;
  }

  private sealed class LumpedNode : BlockNetworkNode {
    private readonly BlockFacing _arm;

    public LumpedNode(BlockFacing? arm = null) {
      _arm = arm ?? BlockFacing.UP;
      Type = "straight";
      Orientation = _arm.Code[..1];
    }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) => face == _arm;
  }
}
