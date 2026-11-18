using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The server's side of a fix: what is checked before anything is taken, what is taken and recorded, and where a placement outside the handler is refused.</summary>
public class HandAssemblyFixTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);
  private static readonly BlockPos East = Cell.AddCopy(BlockFacing.EAST);

  private const string SpecJson =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":2}}""";

  #region Checks before anything is taken

  // Fails when a candidate the server's own list does not hold is placed.
  [Fact]
  public void A_candidate_the_server_does_not_offer_is_refused_and_nothing_is_taken() {
    var scene = Scene.Standing(["ns", "we", "ne"]);
    scene.Pipe(North, "ns");
    scene.Bolts(10);

    AssemblyResult? result = scene.Fix("we", North, BlockFacing.SOUTH);

    Assert.Equal(HandAssemblyServer.NotOffered, result!.Code);
    scene.AssertNothingTaken(10);
  }

  // Fails when the joints are paid per piece instead of per joint made.
  [Fact]
  public void A_fix_pays_per_joint_it_makes_and_records_each_joint_on_its_face() {
    var scene = Scene.Standing(["nw", "nes"]);
    scene.Pipe(North, "ns");
    scene.Pipe(East, "we");
    scene.Bolts(10);

    AssemblyResult? result = scene.Fix("nes", Ground, BlockFacing.UP);

    Assert.Null(result);
    Assert.Equal(6, scene.Carried);
    Assert.Equal(0, scene.Parts);
    BEBehaviorAssembled record = scene.RecordAt(Cell);
    Assert.True(record.Fixed);
    Assert.Equal(2, record.FastenerAt(BlockFacing.NORTH)!.StackSize);
    Assert.Equal(2, record.FastenerAt(BlockFacing.EAST)!.StackSize);
    Assert.Null(record.FastenerAt(BlockFacing.SOUTH));
  }

  // Fails when the cell is not checked before payment: the second fix is refused as not offered, not as occupied.
  [Fact]
  public void A_second_fix_at_a_taken_cell_is_refused_before_it_pays() {
    var scene = Scene.Standing(["ns", "we"]);
    scene.Bolts(10);
    scene.Parts = 2;
    Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));
    int boltsAfterFirst = scene.Carried;

    AssemblyResult? second = scene.Fix("we", Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.Occupied, second!.Code);
    Assert.Equal(boltsAfterFirst, scene.Carried);
    Assert.Equal(1, scene.Parts);
  }

  // Fails when the packet's dimension is used: the aimed cell is in the player's own, whatever the packet says.
  [Fact]
  public void A_fix_reads_the_players_dimension_not_the_packets() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);

    AssemblyResult? result = HandAssemblyServer.Fix(
      scene.World.World,
      scene.Player.Player,
      new AssemblyFix {
        X = Ground.X,
        Y = Ground.Y,
        Z = Ground.Z,
        Dimension = 1,
        Face = BlockFacing.UP.Index,
        HitX = 0.5,
        HitY = 0.5,
        HitZ = 0.5,
        Slot = 0,
        PartCode = scene.Player.ActiveSlot.Itemstack!.Block.Code.ToString(),
        CandidateCode = "test:pipe-ns",
      }
    );

    Assert.Null(result);
    Assert.NotEqual(0, scene.World.GetBlock(Cell).Id);
  }

  // Fails when the tool in the off hand is not checked.
  [Fact]
  public void A_fix_without_the_tool_in_the_off_hand_is_refused_and_nothing_is_taken() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    scene.Player.HoldOffHand(null);

    AssemblyResult? result = scene.Fix("ns", Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.NeedsTool, result!.Code);
    scene.AssertNothingTaken(10);
  }

  // Fails when any item in the off hand passes for the tool.
  [Fact]
  public void A_fix_with_a_wrong_tool_in_the_off_hand_is_refused_and_nothing_is_taken() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    scene.Player.HoldOffHand(new ItemStack(scene.Hammer));

    AssemblyResult? result = scene.Fix("ns", Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.NeedsTool, result!.Code);
    scene.AssertNothingTaken(10);
  }

  // Fails when fasteners are not counted before the part and the fasteners are taken.
  [Fact]
  public void A_fix_short_of_fasteners_takes_nothing_and_names_the_count() {
    var scene = Scene.Standing(["nw", "nes"]);
    scene.Pipe(North, "ns");
    scene.Pipe(East, "we");
    scene.Bolts(3);

    AssemblyResult? result = scene.Fix("nes", Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.NeedsFasteners, result!.Code);
    Assert.Equal("4", result.Args[0]);
    Assert.Equal("3", result.Args[2]);
    scene.AssertNothingTaken(3);
  }

  // Fails when fasteners come from the hotbar alone.
  [Fact]
  public void Fasteners_come_from_the_hotbar_and_then_the_backpack() {
    var scene = Scene.Standing(["nw", "nes"]);
    scene.Pipe(North, "ns");
    scene.Pipe(East, "we");
    scene.Bolts(3);
    scene.BackpackBolts(5);

    AssemblyResult? result = scene.Fix("nes", Ground, BlockFacing.UP);

    Assert.Null(result);
    Assert.Null(scene.Player.StackInSlot(1));
    Assert.Equal(4, scene.Backpack[0].StackSize);
  }

  // Fails when the player's distance to the cell is not checked.
  [Fact]
  public void A_fix_from_beyond_the_picking_range_is_refused() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    scene.Player.Entity.Pos.SetPos(Cell.X + 40, Cell.Y, Cell.Z);

    Assert.Equal(
      HandAssemblyServer.OutOfRange,
      scene.Fix("ns", Ground, BlockFacing.UP)!.Code
    );
    scene.AssertNothingTaken(10);
  }

  // Fails when the land claim is not asked.
  [Fact]
  public void A_fix_in_a_claimed_cell_is_refused() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    scene
      .World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(false);

    Assert.Equal(
      HandAssemblyServer.Claimed,
      scene.Fix("ns", Ground, BlockFacing.UP)!.Code
    );
    scene.AssertNothingTaken(10);
  }

  // Fails when the slot and code the client names are not compared with what is held.
  [Fact]
  public void A_fix_naming_a_part_no_longer_held_is_refused() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);

    Assert.Equal(
      HandAssemblyServer.PartMoved,
      scene.Fix("ns", Ground, BlockFacing.UP, slot: 3)!.Code
    );
    scene.AssertNothingTaken(10);
  }

  // Fails when the part's code is not compared with what is held.
  [Fact]
  public void A_fix_naming_another_part_than_the_one_held_is_refused() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);

    Assert.Equal(
      HandAssemblyServer.PartMoved,
      scene.Fix("ns", Ground, BlockFacing.UP, partCode: "test:other")!.Code
    );
    scene.AssertNothingTaken(10);
  }

  // Fails when the six neighbours of the cell are not required to be loaded.
  [Fact]
  public void A_fix_beside_an_unloaded_chunk_is_refused() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    scene.World.UnloadChunkAt(Cell.AddCopy(BlockFacing.WEST));

    Assert.Equal(
      HandAssemblyServer.Unloaded,
      scene.Fix("ns", Ground, BlockFacing.UP)!.Code
    );
    scene.AssertNothingTaken(10);
  }

  // Fails when a refusal of the network the part would join is not carried to the player.
  [Fact]
  public void A_network_that_refuses_the_member_refuses_the_fix_with_its_code() {
    var scene = Scene.Standing(["ns"]);
    scene.World.RegisterNetwork("test", sys => new RefusingNetwork(sys));
    scene.World.PlaceNode(North, "test", "ns");
    scene.Bolts(10);

    AssemblyResult? result = scene.Fix("ns", North, BlockFacing.SOUTH);

    Assert.Equal("test-refused", result!.Code);
    scene.AssertNothingTaken(10);
  }

  // Fails when the failure code a part's refusal gives is replaced by the generic one.
  [Fact]
  public void A_part_that_refuses_a_candidate_has_its_failure_code_sent_as_it_is() {
    var scene = Scene.Standing(
      ["ns", "we"],
      refusing: "we",
      refusalCode: "wall"
    );

    Assert.Equal("wall", scene.Fix("we", Ground, BlockFacing.UP)!.Code);
  }

  // Fails when a refusal that gives no code is not answered with the generic code.
  [Fact]
  public void A_refusal_with_no_code_is_sent_as_the_generic_one() {
    var scene = Scene.Standing(["ns", "we"], refusing: "we", refusalCode: null);

    Assert.Equal(
      HandAssemblyServer.Refused,
      scene.Fix("we", Ground, BlockFacing.UP)!.Code
    );
  }

  #endregion

  #region What a fix takes and writes

  // Fails when the wrench is not worn by a fix.
  [Fact]
  public void A_fix_wears_the_tool_by_one() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);

    Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));

    Assert.Equal(
      99,
      scene.Wrench.GetRemainingDurability(scene.Player.OffHandSlot.Itemstack)
    );
  }

  // Fails when a block that is not a node is left with no record of its one joint.
  [Fact]
  public void A_block_that_is_not_a_node_records_its_one_joint() {
    var scene = Scene.StandingPlain();
    scene.Bolts(10);

    Assert.Null(scene.Fix("", Ground, BlockFacing.UP));

    BEBehaviorAssembled record = scene.RecordAt(Cell);
    Assert.True(record.Fixed);
    Assert.Equal(2, record.Joint!.StackSize);
    Assert.Equal(8, scene.Carried);
  }

  // Fails when creative pays a fastener, takes the part or wears the tool, or needs the tool.
  [Fact]
  public void In_creative_a_fix_needs_no_tool_and_takes_nothing() {
    var scene = Scene.Standing(["nw", "nes"]);
    scene.Pipe(North, "ns");
    scene.Pipe(East, "we");
    scene.Player.GameMode = EnumGameMode.Creative;
    scene.Player.HoldOffHand(null);

    Assert.Null(scene.Fix("nes", Ground, BlockFacing.UP));

    Assert.Equal(1, scene.Parts);
    BEBehaviorAssembled record = scene.RecordAt(Cell);
    Assert.True(record.Fixed);
    Assert.Null(record.FastenerAt(BlockFacing.NORTH));
  }

  // Fails when a record is not written to the tree or not read back resolved.
  [Fact]
  public void A_record_survives_the_tree() {
    var scene = Scene.Standing(["ns"]);
    var be = new BlockEntityAssembled();
    var record = new BEBehaviorAssembled(be) { Fixed = true };
    record.SetFastener(BlockFacing.SOUTH, new ItemStack(scene.Bolt, 2));
    record.Joint = new ItemStack(scene.Bolt, 1);
    var tree = new TreeAttribute();

    record.ToTreeAttributes(tree);
    var saved = new TreeAttribute();
    saved.FromBytes(tree.ToBytes());
    var read = new BEBehaviorAssembled(be);
    read.FromTreeAttributes(saved, scene.World.World);

    Assert.True(read.Fixed);
    Assert.Equal(2, read.FastenerAt(BlockFacing.SOUTH)!.StackSize);
    Assert.Same(scene.Bolt, read.FastenerAt(BlockFacing.SOUTH)!.Item);
    Assert.Equal(1, read.Joint!.StackSize);
    Assert.Null(read.FastenerAt(BlockFacing.NORTH));
  }

  #endregion

  #region Placing outside the handler

  // Fails when BlockNetworkNode.TryPlaceBlock does not ask the gate.
  [Fact]
  public void A_node_placed_in_survival_outside_the_handler_is_refused() {
    var scene = Scene.Standing(["ns"]);
    scene.Player.GameMode = EnumGameMode.Survival;
    string code = "";

    bool placed = scene
      .Part("ns")
      .TryPlaceBlock(
        scene.World.World,
        scene.Player.Player,
        new ItemStack(scene.Part("ns")),
        Scene.Select(Ground, BlockFacing.UP),
        ref code
      );

    Assert.False(placed);
    Assert.Equal("__ignore__", code);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
    Assert.Contains(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call =>
        call.GetMethodInfo().Name == "SendIngameError"
        && (string)call.GetArguments()[0]! == HandAssemblyServer.NeedsTool
    );
  }

  // Fails when the behaviour does not ask the gate, or when it lets a later behaviour place the block.
  [Fact]
  public void A_plain_block_and_an_orientable_one_are_refused_in_survival_outside_the_handler() {
    var scene = Scene.Standing(["ns"]);
    scene.Player.GameMode = EnumGameMode.Survival;

    foreach (Block block in new[] { scene.PlainPart, scene.ChairPart }) {
      string code = "";
      bool placed = block.TryPlaceBlock(
        scene.World.World,
        scene.Player.Player,
        new ItemStack(block),
        Scene.Select(Ground, BlockFacing.UP),
        ref code
      );

      Assert.False(placed);
      Assert.Equal("__ignore__", code);
      Assert.Equal(0, scene.World.GetBlock(Cell).Id);
    }
  }

  // Fails when the chosen token is not stored for the block entity to take from the placement.
  [Fact]
  public void A_fix_stores_the_placed_token_as_the_only_orientation_choice() {
    var scene = Scene.Standing(["ns", "we"]);
    scene.Bolts(10);
    string[]? seen = null;
    foreach (Block pipe in new[] { scene.Part("ns"), scene.Part("we") }) {
      var probe = new StoreProbe(pipe, found => seen = found);
      pipe.BlockBehaviors = [probe];
      pipe.CollectibleBehaviors = [probe];
    }

    Assert.Null(scene.Fix("we", Ground, BlockFacing.UP));

    Assert.Equal(["we"], seen);
  }

  // Fails when creative placement does not set the first candidate fixed with nothing recorded.
  [Fact]
  public void In_creative_placement_sets_the_first_candidate_fixed_and_free() {
    var scene = Scene.Standing(["we", "ns"]);
    scene.Player.GameMode = EnumGameMode.Creative;
    scene.Player.Looking(BlockFacing.EAST);
    scene.Bolts(10);
    string first = scene.Offer(Ground, BlockFacing.UP)[0].Token;
    string code = "";

    bool placed = scene
      .Part("we")
      .TryPlaceBlock(
        scene.World.World,
        scene.Player.Player,
        new ItemStack(scene.Part("we")),
        Scene.Select(Ground, BlockFacing.UP),
        ref code
      );

    Assert.True(placed);
    Assert.Equal(
      first,
      ((BlockNetworkNode)scene.World.GetBlock(Cell)).Orientation
    );
    Assert.True(scene.RecordAt(Cell).Fixed);
    Assert.Equal(10, scene.Carried);
  }

  // Fails when a creative aim that offers nothing is placed anyway.
  [Fact]
  public void In_creative_an_aim_with_no_candidate_places_nothing() {
    var scene = Scene.Standing(["we"], refusing: "we", refusalCode: "wall");
    scene.Player.GameMode = EnumGameMode.Creative;
    string code = "";

    bool placed = scene
      .Part("we")
      .TryPlaceBlock(
        scene.World.World,
        scene.Player.Player,
        new ItemStack(scene.Part("we")),
        Scene.Select(Ground, BlockFacing.UP),
        ref code
      );

    Assert.False(placed);
    Assert.Equal(HandAssemblyServer.NotOffered, code);
  }

  #endregion

  private sealed class StoreProbe(Block block, System.Action<string[]?> found)
    : BlockBehavior(block) {
    private static readonly System.Reflection.FieldInfo Store =
      typeof(BlockNetworkNode).GetField(
        "_tempOrientationsStore",
        System.Reflection.BindingFlags.NonPublic
          | System.Reflection.BindingFlags.Static
      )!;

    public override void OnBlockPlaced(
      IWorldAccessor world,
      BlockPos blockPos,
      ref EnumHandling handling
    ) {
      foreach (
        System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)
          Store.GetValue(null)!
      )
        found((string[])entry.Value!);
    }
  }

  private sealed class RefusingNetwork(BlockNetworkModSystem system)
    : BlockNetwork(system),
      INetworkPlacementCheck {
    public override string NetworkType => "test";

    public bool AcceptsMember(
      IBlockAccessor world,
      BlockPos pos,
      Block block,
      out string failureCode
    ) {
      failureCode = "test-refused";
      return false;
    }

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) { }

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) { }
  }

  internal sealed class AsmPipe : BlockNetworkNode, IHandAssemblyPart {
    private readonly string? _refusing;
    private readonly string? _refusalCode;

    public AsmPipe(
      string token,
      string[] tokens,
      string? refusing,
      string? refusalCode
    ) {
      Type = "mixed";
      Orientation = token;
      _refusing = refusing;
      _refusalCode = refusalCode;
      AllowedOrientations = new() { ["mixed"] = tokens };
    }

    public override string NetworkType => "test";

    public bool Cracked { get; set; }

    public override Dictionary<string, string[]> AllowedOrientations { get; }

    protected internal override bool IsCrackedAt(
      IWorldAccessor world,
      BlockPos pos
    ) => Cracked;

    public bool Allows(
      IWorldAccessor world,
      BlockPos pos,
      Block candidate,
      out string? failureCode
    ) {
      failureCode = null;
      if (candidate is AsmPipe pipe && pipe.Orientation == _refusing) {
        failureCode = _refusalCode;
        return false;
      }
      return true;
    }
  }

  internal sealed class Scene {
    private const string Factory = "test.asm";
    private readonly TestWorld _world;
    private readonly Dictionary<string, Block> _variants = [];
    private readonly Block _held;
    private int _nextId = 100;

    private Scene(TestWorld world, TestPlayer player, Block held) {
      _world = world;
      Player = player;
      _held = held;
      Wrench = world.RegisterItem("test:wrench-iron");
      Wrench.Durability = 100;
      Wrench.StorageFlags =
        EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand;
      Hammer = world.RegisterItem("test:hammer-iron");
      Hammer.StorageFlags =
        EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand;
      Bolt = world.RegisterItem("test:bolt");
      player.HoldOffHand(new ItemStack(Wrench));
      player.Hold(new ItemStack(held));
      player.Player.InventoryManager.Inventories["backpack-test"] =
        TestInventory.Of(world, 4, "backpack-test");
    }

    public TestWorld World => _world;
    public TestPlayer Player { get; }
    public Item Wrench { get; }
    public Item Hammer { get; }
    public Item Bolt { get; }
    public Block PlainPart { get; private set; } = null!;
    public Block ChairPart { get; private set; } = null!;

    public InventoryGeneric Backpack =>
      (InventoryGeneric)
        Player.Player.InventoryManager.GetOwnInventory("backpack");

    public int Parts {
      get => Player.ActiveSlot.StackSize;
      set => Player.ActiveSlot.Itemstack!.StackSize = value;
    }

    public int Carried =>
      Player
        .Hotbar.Concat(Backpack)
        .Sum(s => s.Itemstack?.Item == Bolt ? s.StackSize : 0);

    public static Scene Standing(
      string[] tokens,
      string? refusing = null,
      string? refusalCode = "refused"
    ) {
      TestWorld world = Fresh();
      TestPlayer player = world.Player();
      player.StandAt(Cell, BlockFacing.SOUTH);
      player.GameMode = EnumGameMode.Survival;

      var variants = new Dictionary<string, Block>();
      int id = 100;
      foreach (string token in tokens) {
        var pipe = TestBlocks.Configure(
          new AsmPipe(token, tokens, refusing, refusalCode),
          $"test:pipe-{token}",
          id++,
          ("orientation", token)
        );
        pipe.EntityClass = Factory;
        pipe.Attributes = new JsonObject(JToken.Parse(SpecJson));
        world.Register(pipe);
        variants[token] = pipe;
      }

      var scene = new Scene(world, player, variants[tokens[0]]);
      foreach ((string token, Block block) in variants)
        scene._variants[token] = block;
      scene._nextId = id;
      scene.AddOtherParts();
      return scene;
    }

    public static Scene StandingPlain() {
      Scene scene = Standing(["ns"]);
      scene.Player.Hold(new ItemStack(scene.PlainPart));
      return scene;
    }

    private static TestWorld Fresh() {
      var world = new TestWorld();
      world.Place(Ground, TestBlocks.Configure(new Block(), "game:rock", 5));
      world.RegisterBlockEntityFactory(
        Factory,
        () => {
          var be = new BlockEntityAssembled();
          be.Behaviors.Add(new BEBehaviorAssembled(be));
          return be;
        }
      );
      return world;
    }

    private void AddOtherParts() {
      PlainPart = TestBlocks.Configure(new Block(), "test:plain", _nextId++);
      PlainPart.EntityClass = Factory;
      PlainPart.Attributes = new JsonObject(JToken.Parse(SpecJson));
      PlainPart.BlockBehaviors = [new BlockBehaviorHandAssembly(PlainPart)];
      PlainPart.CollectibleBehaviors = [.. PlainPart.BlockBehaviors];
      _world.Register(PlainPart);

      foreach (string side in new[] { "n", "e", "s", "w" }) {
        Block chair = TestBlocks.Configure(
          new Block(),
          $"test:chair-{side}",
          _nextId++,
          ("side", side)
        );
        chair.Attributes = new JsonObject(JToken.Parse(SpecJson));
        var orientable = new BlockBehaviorExOrientable(chair);
        orientable.Initialize(
          new JsonObject(JToken.Parse("""{"mode":"horizontal"}"""))
        );
        chair.BlockBehaviors =
        [
          new BlockBehaviorHandAssembly(chair),
          orientable,
        ];
        chair.CollectibleBehaviors = [.. chair.BlockBehaviors];
        _world.Register(chair);
        if (side == "n")
          ChairPart = chair;
      }
    }

    public Block Part(string token) => _variants[token];

    public void Pipe(BlockPos pos, string token) =>
      _world.Place(
        pos,
        TestBlocks.Configure(
          new AsmPipe(token, [token], null, null),
          $"test:neighbour-{token}",
          _nextId++,
          ("orientation", token)
        )
      );

    public void Bolts(int count) =>
      Player.Hotbar[1].Itemstack = new ItemStack(Bolt, count);

    public void BackpackBolts(int count) =>
      Backpack[0].Itemstack = new ItemStack(Bolt, count);

    public BEBehaviorAssembled RecordAt(BlockPos pos) =>
      _world.GetBlockEntity(pos)!.GetBehavior<BEBehaviorAssembled>();

    public void AssertNothingTaken(int bolts) {
      Assert.Equal(bolts, Carried);
      Assert.Equal(1, Parts);
      if (Player.OffHandSlot.Itemstack is { } tool)
        Assert.Equal(100, Wrench.GetRemainingDurability(tool));
      Assert.Equal(0, _world.GetBlock(Cell).Id);
    }

    public IReadOnlyList<AssemblyCandidate> Offer(
      BlockPos aimed,
      BlockFacing face
    ) =>
      HandAssemblyCandidates.Of(
        _world.World,
        Player.Player,
        _held,
        Select(aimed, face)
      );

    public AssemblyResult? Fix(
      string token,
      BlockPos aimed,
      BlockFacing face,
      int slot = 0,
      string? partCode = null
    ) =>
      HandAssemblyServer.Fix(
        _world.World,
        Player.Player,
        new AssemblyFix {
          X = aimed.X,
          Y = aimed.Y,
          Z = aimed.Z,
          Face = face.Index,
          HitX = 0.5,
          HitY = 0.5,
          HitZ = 0.5,
          Slot = slot,
          PartCode =
            partCode ?? Player.ActiveSlot.Itemstack!.Block.Code.ToString(),
          CandidateCode =
            token == ""
              ? Player.ActiveSlot.Itemstack!.Block.Code.ToString()
              : $"test:pipe-{token}",
        }
      );

    public static BlockSelection Select(BlockPos aimed, BlockFacing face) =>
      new() {
        Position = aimed.Copy(),
        Face = face,
        HitPosition = new Vec3d(0.5, 0.5, 0.5),
      };
  }
}
