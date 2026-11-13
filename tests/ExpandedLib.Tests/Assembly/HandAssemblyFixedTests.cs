using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Blocks;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>A fixed piece: what keeps its token, what the wrench and the break do to it, and the take-off that returns it with its fasteners.</summary>
public class HandAssemblyFixedTests {
  private static readonly string[] Axis = ["ns", "we", "ud"];
  private static readonly BlockPos Centre = new(0, 0, 0);
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);

  private const string SpecJson =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":2}}""";

  #region Tokens and the wrench

  // Fails when a fixed piece is re-oriented by a neighbour change.
  [Fact]
  public void A_fixed_piece_keeps_its_token_when_a_neighbour_changes() {
    var scene = Nodes.Standing("ns", fixedPiece: true);
    scene.World.PlaceNode(new BlockPos(1, 0, 0), "test", "we", id: 50);

    scene.NeighbourChanged();

    Assert.Equal("test:node-ns", scene.CodeAtCentre);
  }

  // Fails when the fixed gate is applied to every node, so a free piece no longer re-orients.
  [Fact]
  public void A_free_piece_still_re_orients_when_a_neighbour_changes() {
    var scene = Nodes.Standing("ns", fixedPiece: false);
    scene.World.PlaceNode(new BlockPos(1, 0, 0), "test", "we", id: 50);

    scene.NeighbourChanged();

    Assert.Equal("test:node-we", scene.CodeAtCentre);
  }

  // Fails when the neighbour-change handler returns for a fixed piece before its support rule.
  [Fact]
  public void A_fixed_piece_with_no_neighbour_and_no_surface_still_breaks() {
    var scene = Nodes.Standing("ns", fixedPiece: true, ground: false);

    scene.NeighbourChanged();

    Assert.Equal(0, scene.World.GetBlock(Centre).Id);
  }

  // Fails when the wrench cycles a fixed piece.
  [Fact]
  public void The_wrench_does_not_cycle_a_fixed_piece() {
    var scene = Nodes.Standing("ns", fixedPiece: true);

    scene.Wrench();

    Assert.Equal("test:node-ns", scene.CodeAtCentre);
  }

  // Fails when a plain click says nothing, or a sneaking click, which starts the take-off hold, says it too.
  [Theory]
  [InlineData(false, 1)]
  [InlineData(true, 0)]
  public void A_plain_wrench_click_on_a_fixed_piece_says_so(
    bool sneaking,
    int messages
  ) {
    var scene = Nodes.Standing("ns", fixedPiece: true);
    scene.Player.Sneaking = sneaking;

    scene.Wrench();

    Assert.Equal(
      messages,
      scene
        .Player.ServerPlayer!.ReceivedCalls()
        .Count(call =>
          call.GetMethodInfo().Name == "SendIngameError"
          && (string)call.GetArguments()[0]! == HandAssemblyServer.Fixed
        )
    );
  }

  // Fails when a piece set with no stored choice is recomputed and left free instead of fixed on its token.
  [Fact]
  public void A_piece_set_by_a_world_set_is_fixed_on_the_token_it_was_set_with() {
    var scene = Nodes.Standing("we", fixedPiece: false, placed: false);

    scene.Block.OnBlockPlaced(scene.World.World, Centre);

    Assert.True(scene.Record.Fixed);
    Assert.Equal(["we"], scene.Entity.PossibleOrientations);
  }

  // Fails when a node that never opted in is fixed by a world set.
  [Fact]
  public void A_node_that_did_not_opt_in_is_not_fixed_by_a_world_set() {
    var scene = Nodes.Standing(
      "we",
      fixedPiece: false,
      placed: false,
      optedIn: false
    );

    scene.Block.OnBlockPlaced(scene.World.World, Centre);

    Assert.False(scene.Record.Fixed);
  }

  #endregion

  #region Taking off

  // Fails when the take-off returns only the fasteners the piece's own record holds.
  [Fact]
  public void Taking_a_piece_off_returns_it_and_the_fasteners_a_neighbour_holds_toward_it() {
    var scene = TwoPieces();

    Assert.Null(scene.TakeOff(Cell));

    Assert.Equal(10, scene.Carried);
    Assert.Equal(1, scene.Pieces);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
    Assert.Null(scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH));
    Assert.Equal(
      99,
      scene.Wrench.GetRemainingDurability(scene.Player.ActiveSlot.Itemstack)
    );
  }

  // Fails when the take-off returns only the fasteners a neighbour holds, not the piece's own.
  [Fact]
  public void Taking_a_piece_off_returns_the_fasteners_its_own_joints_hold() {
    var scene = TwoPieces();

    Assert.Null(scene.TakeOff(North));

    Assert.Equal(10, scene.Carried);
    Assert.Equal(1, scene.Pieces);
    Assert.Equal(0, scene.World.GetBlock(North).Id);
  }

  // Fails when a block that is not a node returns nothing of its record.
  [Fact]
  public void A_block_that_is_not_a_node_returns_the_fasteners_its_record_holds() {
    var scene = new TakeOffScene(Scene.StandingPlain());
    scene.Bolts(10);
    Assert.Null(scene.Fix("", Ground, BlockFacing.UP));
    Assert.Equal(8, scene.Carried);

    Assert.Null(scene.TakeOff(Cell));

    Assert.Equal(10, scene.Carried);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
  }

  // Fails when a block set with no record returns the def's perJoint of fasteners, or its piece is lost.
  [Fact]
  public void A_block_with_no_record_returns_only_the_piece() {
    var scene = new TakeOffScene(Scene.StandingPlain());
    scene.Bolts(10);
    scene.PlainPart.Drops =
    [
      new BlockDropItemStack(new ItemStack(scene.PlainPart)),
    ];
    scene.World.Place(Cell, scene.PlainPart);

    Assert.Null(scene.TakeOff(Cell));

    Assert.Equal(10, scene.Carried);
    Assert.Equal(1, scene.PlainPieces);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
  }

  // Fails when a cracked piece is returned with its fasteners.
  [Fact]
  public void A_cracked_piece_comes_off_as_its_fasteners_alone() {
    var scene = TwoPieces();
    ((HandAssemblyFixTests.AsmPipe)scene.World.GetBlock(North)).Cracked = true;

    Assert.Null(scene.TakeOff(North));

    Assert.Equal(10, scene.Carried);
    Assert.Equal(0, scene.Pieces);
  }

  // Fails when the node stays in its network after it is taken off.
  [Fact]
  public void A_node_taken_off_leaves_its_network() {
    var scene = Nodes.Standing("ns", fixedPiece: true);
    scene.Player.StandAt(Centre, BlockFacing.SOUTH);
    Assert.NotNull(scene.World.NetworkAt(Centre));
    Item wrench = scene.World.RegisterItem("test:wrench-iron");
    wrench.Durability = 100;
    scene.Player.Hold(new ItemStack(wrench));
    scene.Player.Sneaking = true;

    Assert.Null(
      HandAssemblyServer.TakeOff(
        scene.World.World,
        scene.Player.Player,
        new AssemblyTakeOff {
          X = 0,
          Y = 0,
          Z = 0,
        }
      )
    );

    Assert.Null(scene.World.NetworkAt(Centre));
  }

  // Fails when the range guard is dropped; the input is fixed, held, sneaking, claimed-free and only too far.
  [Fact]
  public void A_take_off_from_beyond_the_picking_range_is_refused() {
    var scene = TwoPieces();
    scene.Player.Entity.Pos.SetPos(Cell.X + 40, Cell.Y, Cell.Z);

    Assert.Equal(HandAssemblyServer.OutOfRange, scene.TakeOff(Cell)!.Code);

    scene.AssertStillFixed(Cell);
  }

  // Fails when the land claim is not asked.
  [Fact]
  public void A_take_off_in_a_claimed_cell_is_refused() {
    var scene = TwoPieces();
    scene
      .World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(false);

    Assert.Equal(HandAssemblyServer.Claimed, scene.TakeOff(Cell)!.Code);

    scene.AssertStillFixed(Cell);
  }

  // Fails when the tool in the main hand is not required.
  [Fact]
  public void A_take_off_without_the_tool_in_the_main_hand_is_refused() {
    var scene = TwoPieces();

    Assert.Equal(
      HandAssemblyServer.NeedsToolMain,
      scene.TakeOff(Cell, tool: false)!.Code
    );

    scene.AssertStillFixed(Cell);
  }

  // Fails when the sneak is not required.
  [Fact]
  public void A_take_off_without_sneaking_is_refused() {
    var scene = TwoPieces();

    Assert.Equal(
      HandAssemblyServer.Fixed,
      scene.TakeOff(Cell, sneak: false)!.Code
    );

    scene.AssertStillFixed(Cell);
  }

  // Fails when a piece that was never fixed is taken off by the wrench.
  [Fact]
  public void A_take_off_of_a_piece_that_is_not_fixed_is_refused() {
    var scene = TwoPieces();
    scene.RecordAt(Cell).Fixed = false;

    Assert.Equal(HandAssemblyServer.NotFixed, scene.TakeOff(Cell)!.Code);

    scene.AssertStillFixed(Cell, fixedFlag: false);
  }

  // Fails when creative takes the piece and fasteners back, or wears the tool.
  [Fact]
  public void In_creative_a_take_off_returns_nothing() {
    var scene = TwoPieces();
    scene.Player.GameMode = EnumGameMode.Creative;

    Assert.Null(scene.TakeOff(Cell));

    Assert.Equal(8, scene.Carried);
    Assert.Equal(0, scene.Pieces);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
    Assert.Equal(
      100,
      scene.Wrench.GetRemainingDurability(scene.Player.ActiveSlot.Itemstack)
    );
  }

  #endregion

  #region Breaking and saving

  // Fails when breaking a fixed piece returns its fasteners, or leaves its neighbour's record toward it.
  [Fact]
  public void Breaking_a_fixed_piece_loses_its_fasteners_and_clears_the_records_toward_it() {
    var scene = TwoPieces();

    scene.World.Accessor.BreakBlock(Cell, null);

    Assert.Equal(8, scene.Carried);
    Assert.NotEmpty(scene.World.Drops);
    Assert.All(
      scene.World.Drops,
      drop => Assert.NotSame(scene.Bolt, drop.Item)
    );
    Assert.Null(scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH));
  }

  // Fails when breaking a piece returns the fasteners its own record holds.
  [Fact]
  public void Breaking_a_piece_loses_the_fasteners_of_its_own_joints() {
    var scene = TwoPieces();

    scene.World.Accessor.BreakBlock(North, null);

    Assert.Equal(8, scene.Carried);
    Assert.All(
      scene.World.Drops,
      drop => Assert.NotSame(scene.Bolt, drop.Item)
    );
  }

  // Fails when the joint records are not written to the block entity's tree, or not read back.
  [Fact]
  public void A_reloaded_piece_keeps_its_fixed_flag_and_joint_records() {
    var scene = TwoPieces();

    scene.World.Reload(North);

    BEBehaviorAssembled record = scene.RecordAt(North);
    Assert.True(record.Fixed);
    Assert.Equal(2, record.FastenerAt(BlockFacing.SOUTH)!.StackSize);
  }

  #endregion

  #region Weeping pipes

  // Fails when the wrench click on a fixed, weeping pipe leaves the weep open.
  [Fact]
  public void A_plain_wrench_click_re_makes_the_weeping_joint_of_a_fixed_pipe() {
    var scene = Pipes.Laid();
    scene.Weep();

    scene.Wrench(0);

    Assert.False(scene.Store.IsWeeping(scene.Edge));
    Assert.DoesNotContain(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call => call.GetMethodInfo().Name == "SendIngameError"
    );
  }

  // Fails when a pipe's cracked joint is not reported, or the wrench re-makes it.
  [Fact]
  public void A_cracked_pipe_is_reported_and_the_wrench_does_not_re_make_it() {
    var scene = Pipes.Laid();
    scene.Crack();

    Assert.True(scene.Pipe.IsCrackedAt(scene.World.World, Pipes.Cell(0)));
    scene.Wrench(0);

    Assert.True(scene.Store.IsWeeping(scene.Edge));
    Assert.Contains(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call =>
        call.GetMethodInfo().Name == "SendIngameError"
        && (string)call.GetArguments()[0]! == HandAssemblyServer.Fixed
    );
  }

  // Fails when a sound pipe is reported cracked.
  [Fact]
  public void A_sound_pipe_is_not_reported_cracked() {
    var scene = Pipes.Laid();
    scene.Weep();

    Assert.False(scene.Pipe.IsCrackedAt(scene.World.World, Pipes.Cell(0)));
  }

  #endregion

  private static TakeOffScene TwoPieces() {
    var scene = new TakeOffScene(Scene.Standing(["ns"]));
    scene.Bolts(10);
    scene.Parts = 2;
    Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));
    Assert.Null(scene.Fix("ns", Cell, BlockFacing.NORTH));
    Assert.Equal(8, scene.Carried);
    scene.Player.Hold(new ItemStack(scene.Wrench));
    scene.Player.Sneaking = true;
    return scene;
  }

  private sealed class TakeOffScene(Scene inner) {
    public TestWorld World => inner.World;
    public TestPlayer Player => inner.Player;
    public Item Wrench => inner.Wrench;
    public Item Bolt => inner.Bolt;
    public Block PlainPart => inner.PlainPart;
    public int Carried => inner.Carried;

    public int Parts {
      set => inner.Parts = value;
    }

    public void Bolts(int count) => inner.Bolts(count);

    public AssemblyResult? Fix(
      string token,
      BlockPos aimed,
      BlockFacing face
    ) => inner.Fix(token, aimed, face);

    public BEBehaviorAssembled RecordAt(BlockPos pos) => inner.RecordAt(pos);

    public int Pieces =>
      CountOf(s => s.Block?.Code.Path.StartsWith("pipe-") == true);

    public int PlainPieces => CountOf(s => s.Block == PlainPart);

    private int CountOf(System.Func<ItemStack, bool> match) =>
      Player.Hotbar.Sum(slot =>
        slot.Itemstack is { } stack && match(stack) ? stack.StackSize : 0
      );

    public AssemblyResult? TakeOff(
      BlockPos pos,
      bool sneak = true,
      bool tool = true
    ) {
      Player.Hold(tool ? new ItemStack(Wrench) : null);
      Player.Sneaking = sneak;
      return HandAssemblyServer.TakeOff(
        World.World,
        Player.Player,
        new AssemblyTakeOff {
          X = pos.X,
          Y = pos.Y,
          Z = pos.Z,
        }
      );
    }

    public void AssertStillFixed(BlockPos pos, bool fixedFlag = true) {
      Assert.NotEqual(0, World.GetBlock(pos).Id);
      Assert.Equal(fixedFlag, RecordAt(pos).Fixed);
      Assert.Equal(8, Carried);
      Assert.Equal(0, Pieces);
      if (Player.ActiveSlot.Itemstack is { } held)
        Assert.Equal(100, Wrench.GetRemainingDurability(held));
    }
  }

  private sealed class Nodes {
    private readonly TestNetworkBlock[] _family;

    private Nodes(
      TestWorld world,
      TestPlayer player,
      TestNetworkBlock[] family,
      OrientableNode entity,
      BEBehaviorAssembled record,
      string token
    ) {
      World = world;
      Player = player;
      _family = family;
      Entity = entity;
      Record = record;
      Token = token;
    }

    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public OrientableNode Entity { get; }
    public BEBehaviorAssembled Record { get; }
    private string Token { get; }

    public Block Block => _family.First(b => b.Orientation == Token);

    public string? CodeAtCentre => World.GetBlock(Centre).Code?.ToString();

    public static Nodes Standing(
      string token,
      bool fixedPiece,
      bool ground = true,
      bool placed = true,
      bool optedIn = true
    ) {
      var world = new TestWorld();
      world.RegisterNetwork("test", sys => new StubNetwork(sys));
      TestPlayer player = world.Player();
      player.GameMode = EnumGameMode.Survival;
      if (ground)
        world.Place(
          Centre.AddCopy(BlockFacing.DOWN),
          TestBlocks.Configure(new Block(), "game:rock", 5)
        );

      TestNetworkBlock[] family = TestNetworkBlock.Family(
        "test",
        "test:node",
        "Axis",
        Axis
      );
      foreach (TestNetworkBlock block in family) {
        if (optedIn)
          block.Attributes = new JsonObject(JToken.Parse(SpecJson));
        world.Register(block);
      }

      var entity = new OrientableNode {
        Orientation = token,
        PossibleOrientations = Axis,
      };
      var record = new BEBehaviorAssembled(entity) { Fixed = fixedPiece };
      entity.Behaviors.Add(record);
      world.Place(Centre, family.First(b => b.Orientation == token), entity);
      if (placed)
        world.Initialize(entity);
      return new Nodes(world, player, family, entity, record, token);
    }

    public void NeighbourChanged() =>
      ((BlockNetworkNode)World.GetBlock(Centre)).OnNeighbourBlockChange(
        World.World,
        Centre,
        new BlockPos(1, 0, 0)
      );

    public void Wrench() =>
      ((BlockNetworkNode)World.GetBlock(Centre)).Rotate(
        Player.Entity,
        new BlockSelection { Position = Centre.Copy(), Face = BlockFacing.UP },
        1
      );
  }

  private sealed class FixedFlowCell : BlockEntityNetworkNode {
    public override string NetworkType {
      get => "pipeflow";
      set { }
    }

    public FixedFlowCell() {
      Behaviors.Add(new CastCell(this));
      Behaviors.Add(new BEBehaviorAssembled(this) { Fixed = true });
    }
  }

  private sealed class CastCell(BlockEntity be) : BEBehaviorPipeFlowCell(be) {
    public override FlowBore? Bore => Pipes.CastMain;
  }

  private sealed class Pipes {
    public static readonly FlowBore CastMain = new(
      0.25,
      5,
      FlowBore.BurstFor(5),
      4,
      0.03,
      BlockPipe.FlangedJoint,
      90,
      0.015
    );

    private const double Atm = 101325;

    private Pipes(TestWorld world, TestPlayer player, BlockPipe pipe) {
      World = world;
      Player = player;
      Pipe = pipe;
    }

    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public BlockPipe Pipe { get; }

    public PipeFlowStore Store =>
      ((PipeFlowNetwork)World.NetworkAt(Cell(0))!).Store;

    public int Edge =>
      Store.Nodes.ContainsKey(NodeKey.Centre(Cell(0)))
      && Store.Nodes.ContainsKey(NodeKey.Centre(Cell(1)))
        ? Store.EdgeBetween(
          Store.IndexOf(NodeKey.Centre(Cell(0))),
          Store.IndexOf(NodeKey.Centre(Cell(1)))
        )
        : -1;

    private bool IsWeeping => Edge >= 0 && Store.IsWeeping(Edge);

    public static BlockPos Cell(int x) => new(x, 0, 0);

    public static Pipes Laid() {
      var world = new TestWorld();
      world.RegisterNetwork("pipeflow", sys => new PipeFlowNetwork(sys));
      TestPlayer player = world.Player();
      player.GameMode = EnumGameMode.Survival;
      double volume = System.Math.PI / 4 * 0.25 * 0.25;
      for (int x = 0; x < 2; x++) {
        var be = new FixedFlowCell();
        var tree = new TreeAttribute();
        PipeFlowSave.Write(
          tree,
          0,
          new PipeNodeSave(
            9 * Atm * volume / (287 * 293.15),
            0,
            0,
            20,
            20,
            0,
            0
          )
        );
        world.Place(
          Cell(x),
          TestNetworkBlock.Create("pipeflow", "we", 1 + x),
          be
        );
        be.GetBehavior<BEBehaviorPipeFlowCell>()
          .FromTreeAttributes(tree, world.World);
        world.Initialize(be);
      }

      BlockPipe pipe = TestBlocks.Configure(
        new BlockPipe(),
        "test:pipe-straight",
        90
      );
      pipe.SetNetworkTypeForTest("straight");
      pipe.ApplyOrientationForTest("we");
      var scene = new Pipes(world, player, pipe);
      var run = (PipeFlowNetwork)world.NetworkAt(Cell(0))!;
      for (int i = 0; i < 4 && scene.Edge < 0; i++)
        run.OnTick(world.Accessor, 0.05f, world.Networks);
      Assert.True(scene.Edge >= 0);
      return scene;
    }

    public void Weep() => Damage(PipeFlowStore.WeepingBit);

    public void Crack() =>
      Damage(PipeFlowStore.CrackedBit | PipeFlowStore.WeepingBit);

    private void Damage(int bits) {
      Store.RestoreDamage(
        new Dictionary<NodeKey, byte> {
          [NodeKey.Centre(Cell(0))] = (byte)bits,
          [NodeKey.Centre(Cell(1))] = (byte)bits,
        }
      );
      Assert.True(IsWeeping);
    }

    public void Wrench(int x) =>
      Pipe.Rotate(
        Player.Entity,
        new BlockSelection { Position = Cell(x), Face = BlockFacing.UP },
        1
      );
  }
}
