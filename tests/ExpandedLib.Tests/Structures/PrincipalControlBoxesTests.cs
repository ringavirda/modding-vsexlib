using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A principal answers its own cell from <c>principalCell</c>: a mega-block, a one-cell node, a
/// host node turned as its fillers are, and a plain block through the static pair.</summary>
public class PrincipalControlBoxesTests {
  private static readonly BlockPos Pos = new(10, 5, 10);
  private static readonly Cuboidf Full = new(0f, 0f, 0f, 1f, 1f, 1f);

  private const string Cell = """
    {
      "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 0.5, "y2": 0.25, "z2": 0.75 }],
      "selectionBoxes": [
        { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0.5, "z2": 1 },
        { "x1": 0.1, "y1": 0.5, "z1": 0.2, "x2": 0.4, "y2": 0.9, "z2": 0.6 }
      ],
      "controls": [{ "id": "dial", "box": { "x1": 0.1, "y1": 0.5, "z1": 0.2, "x2": 0.4, "y2": 0.9, "z2": 0.6 } }]
    }
    """;

  private const string DoorCell = """
    {
      "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0.25, "z2": 1 }],
      "doors": [{
        "control": "hatch",
        "shut": [{ "x1": 0.75, "y1": 0.25, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
        "open": [{ "x1": 0, "y1": 0.25, "z1": 0.75, "x2": 1, "y2": 1, "z2": 1 }],
        "opening": [],
        "openCollides": false
      }]
    }
    """;

  // Fails when BlockFilledMegastructure's box overrides are removed.
  [Fact]
  public void A_megastructure_principal_answers_its_row() {
    var scene = new MegaScene(Cell);
    MegaBlock block = scene.Block;
    IBlockAccessor accessor = scene.Accessor;

    Cuboidf[] collision = block.GetCollisionBoxes(accessor, Pos);

    Cuboidf box = Assert.Single(collision);
    Assert.Equal(0.25f, box.Y2, 4);
    Assert.Equal(0.75f, box.Z2, 4);
#if GAME_GE_1_22
    Cuboidf[] selection = block.GetSelectionBoxes(accessor, Pos);
    Assert.Equal(2, selection.Length);
    Assert.Equal("dial", Assert.IsType<CuboidfWithId>(selection[0]).Id);
#endif
  }

  // Fails when a block without a principalCell answers an empty array.
  [Fact]
  public void No_row_keeps_the_block_boxes() {
    var scene = new MegaScene(null);
    MegaBlock block = scene.Block;
    IBlockAccessor accessor = scene.Accessor;

    Assert.Same(block.CollisionBoxes, block.GetCollisionBoxes(accessor, Pos));
    Assert.Null(ControlBoxes.Collision(block, accessor, Pos));
  }

  // Fails when neither CellBoxes.Turned nor ControlBoxes.Compose attaches the control ids after RotatedCopy.
  [Theory]
  [InlineData("ns", 0f, 0f, 0f)]
  [InlineData("we", 0f, 90f, 0f)]
  [InlineData("ud", 90f, 0f, 0f)]
  public void A_one_cell_node_keeps_ids_in_every_orientation(
    string orientation,
    float rotX,
    float rotY,
    float rotZ
  ) {
    var node = Prepare(new PlainNode(), Cell, orientation, null);
    var pivot = new Vec3d(0.5, 0.5, 0.5);

    Cuboidf[] collision = node.GetCollisionBoxes(
      Substitute.For<IBlockAccessor>(),
      Pos
    );

    Cuboidf expected = new Cuboidf(0f, 0f, 0f, 0.5f, 0.25f, 0.75f).RotatedCopy(
      rotX,
      rotY,
      rotZ,
      pivot
    );
    AssertBox(expected, Assert.Single(collision));
#if GAME_GE_1_22
    Cuboidf[] selection = node.GetSelectionBoxes(
      Substitute.For<IBlockAccessor>(),
      Pos
    );
    Cuboidf dial = new Cuboidf(0.1f, 0.5f, 0.2f, 0.4f, 0.9f, 0.6f).RotatedCopy(
      rotX,
      rotY,
      rotZ,
      pivot
    );
    AssertBox(dial, selection[0]);
    Assert.Equal("dial", Assert.IsType<CuboidfWithId>(selection[0]).Id);
#endif
  }

  // Fails when a host node's cell is turned by GetRotations instead of StructureAngle.
  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_host_node_turns_its_cell_with_its_fillers(int angle) {
    var node = Prepare(
      new HostNode(),
      Cell,
      "ns",
      ExOrientation.SideFromAngle(angle)
    );
    var row = new Cuboidf(0f, 0f, 0f, 0.5f, 0.25f, 0.75f);

    Cuboidf[] collision = node.GetCollisionBoxes(
      Substitute.For<IBlockAccessor>(),
      Pos
    );

    AssertBox(
      ExOrientation.RotateBoxes([row], angle)[0],
      Assert.Single(collision)
    );
  }

  // Fails when the principal's own collision ignores its entity's answer, or the answer is asked at another cell.
  [Fact]
  public void A_principal_entity_answers_its_own_cell() {
    var scene = new MegaScene(Cell);
    MegaBlock block = scene.Block;
    IBlockAccessor accessor = scene.Accessor;
    Cuboidf answer = new(0.2f, 0f, 0.2f, 0.6f, 0.4f, 0.6f);
    var be = new CellEntity { Pos = Pos, Block = block, Answer = new[] { answer } };
    accessor.GetBlockEntity(Pos).Returns(be);

    AssertBox(answer, Assert.Single(block.GetCollisionBoxes(accessor, Pos)));
    Vec3i offset = Assert.Single(be.Asked);
    Assert.Equal(0, offset.X);
    Assert.Equal(0, offset.Y);
    Assert.Equal(0, offset.Z);
  }

  // Fails when a row with doors answers cached arrays instead of composing from the door state.
  [Fact]
  public void Principal_doors_follow_state() {
    var scene = new MegaScene(DoorCell);
    MegaBlock block = scene.Block;
    IBlockAccessor accessor = scene.Accessor;
    var be = new StatesEntity { Pos = Pos, Block = block };
    accessor.GetBlockEntity(Pos).Returns(be);

    Cuboidf[] shut = block.GetCollisionBoxes(accessor, Pos);
    be.States["hatch"] = ControlState.Open;
    Cuboidf[] open = block.GetCollisionBoxes(accessor, Pos);

    Assert.Equal(2, shut.Length);
    Assert.Single(open);
  }

  // Fails when the static pair ignores the block's StructureAngle.
  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void The_static_pair_turns_a_plain_principal(int angle) {
    var block = TestBlocks.Configure(
      new Block(),
      "test:plain",
      60,
      ("side", ExOrientation.SideFromAngle(angle))
    );
    block.Attributes = Attributes(Cell);
    var row = new Cuboidf(0f, 0f, 0f, 0.5f, 0.25f, 0.75f);

    Cuboidf[]? collision = ControlBoxes.Collision(
      block,
      Substitute.For<IBlockAccessor>(),
      Pos
    );

    AssertBox(
      ExOrientation.RotateBoxes([row], angle)[0],
      Assert.Single(collision!)
    );
  }

  private static JsonObject Attributes(string? cell) =>
    new(
      cell == null
        ? new JObject()
        : new JObject { ["principalCell"] = JObject.Parse(cell) }
    );

  private static NodeBase Prepare(
    NodeBase blank,
    string cell,
    string orientation,
    string? side
  ) {
    var variants =
      side == null
        ? new[] { ("orientation", orientation) }
        : new[] { ("orientation", orientation), ("side", side) };
    var node = TestBlocks.Configure(blank, "test:node", 61, variants);
    node.Attributes = Attributes(cell);
    node.SetNetworkTypeForTest("straight");
    node.ApplyOrientationForTest(orientation);
    node.Precompute();
    return node;
  }

  private static void AssertBox(Cuboidf expected, Cuboidf actual) {
    Assert.Equal(expected.X1, actual.X1, 4);
    Assert.Equal(expected.Y1, actual.Y1, 4);
    Assert.Equal(expected.Z1, actual.Z1, 4);
    Assert.Equal(expected.X2, actual.X2, 4);
    Assert.Equal(expected.Y2, actual.Y2, 4);
    Assert.Equal(expected.Z2, actual.Z2, 4);
  }

  private sealed class MegaBlock : BlockFilledMegastructure { }

  private sealed class MegaScene {
    public readonly MegaBlock Block;
    public readonly IBlockAccessor Accessor = Substitute.For<IBlockAccessor>();

    public MegaScene(string? cell) {
      Block = TestBlocks.Configure(new MegaBlock(), "test:mega", 50);
      Block.Attributes = Attributes(cell);
      Block.CollisionBoxes = [Full];
      Block.SelectionBoxes = [Full];
    }
  }

  private abstract class NodeBase : BlockNetworkNode {
    public override string NetworkType => "test";

    public override Dictionary<string, string[]> AllowedOrientations { get; } =
      new() { { "straight", ["ns", "we", "ud"] } };

    protected override string GetFallbackOrientation(string? type) => "ns";

    public void Precompute() => PrecomputeRotatedBoxes();
  }

  private sealed class PlainNode : NodeBase { }

  private sealed class HostNode : NodeBase, IFillerHost {
    public JsonObject? FillerOffsets => null;
  }

  private sealed class StatesEntity : BlockEntity, IControlStates {
    public readonly Dictionary<string, ControlState> States = [];

    public ControlState? StateOf(string controlId) =>
      States.TryGetValue(controlId, out ControlState state) ? state : null;
  }

  private sealed class CellEntity : BlockEntity, ICellCollision {
    public Cuboidf[]? Answer;
    public readonly List<Vec3i> Asked = [];

    public Cuboidf[]? CollisionAt(Vec3i northOffset, int angle) {
      Asked.Add(northOffset);
      return Answer;
    }
  }
}
