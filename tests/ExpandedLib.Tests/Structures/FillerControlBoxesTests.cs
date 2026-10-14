using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A filler answers its boxes from its row on the principal's definition: found at every facing,
/// composed with the controls' states, falling back to the boxes saved on the cell.</summary>
public class FillerControlBoxesTests {
  private static readonly BlockPos PrincipalPos = new(10, 5, 10);

  private static readonly Cuboidf Full = new(0f, 0f, 0f, 1f, 1f, 1f);

  private static readonly Cuboidf PanelCollision = new(
    0f,
    0f,
    0f,
    0.5f,
    0.5f,
    0.5f
  );
  private static readonly Cuboidf PanelValve = new(
    0f,
    0.5f,
    0f,
    0.5f,
    1f,
    0.5f
  );
  private static readonly Cuboidf PanelBody = new(0.5f, 0f, 0f, 1f, 1f, 1f);
  private static readonly Cuboidf MirrorCollision = new(
    0.5f,
    0f,
    0.5f,
    1f,
    1f,
    1f
  );
  private static readonly Cuboidf DoorBody = new(0f, 0f, 0f, 1f, 0.25f, 1f);
  private static readonly Cuboidf DoorShut = new(0.75f, 0.25f, 0f, 1f, 1f, 1f);
  private static readonly Cuboidf DoorOpen = new(0f, 0.25f, 0.75f, 1f, 1f, 1f);

  private const string Offsets = """
    [
      {
        "x": 1, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 0.5, "y2": 0.5, "z2": 0.5 }],
        "selectionBoxes": [
          { "x1": 0.5, "y1": 0, "z1": 0, "x2": 1, "y2": 1, "z2": 1 },
          { "x1": 0, "y1": 0.5, "z1": 0, "x2": 0.5, "y2": 1, "z2": 0.5 }
        ],
        "controls": [{ "id": "valve", "box": { "x1": 0, "y1": 0.5, "z1": 0, "x2": 0.5, "y2": 1, "z2": 0.5 } }]
      },
      {
        "x": -1, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0.5, "y1": 0, "z1": 0.5, "x2": 1, "y2": 1, "z2": 1 }]
      },
      { "x": 2, "y": 0, "z": 0, "collisionBoxes": [] },
      {
        "x": 3, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0.25, "z2": 1 }],
        "doors": [{
          "control": "hatch",
          "shut": [{ "x1": 0.75, "y1": 0.25, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
          "open": [{ "x1": 0, "y1": 0.25, "z1": 0.75, "x2": 1, "y2": 1, "z2": 1 }],
          "opening": [],
          "openCollides": true
        }]
      },
      {
        "x": 4, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0.25, "z2": 1 }],
        "doors": [{
          "control": "hatch",
          "shut": [{ "x1": 0.75, "y1": 0.25, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
          "open": [{ "x1": 0, "y1": 0.25, "z1": 0.75, "x2": 1, "y2": 1, "z2": 1 }],
          "opening": [],
          "openCollides": false
        }]
      },
      {
        "x": 5, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
        "selectionBoxes": [{ "x1": 0.4, "y1": 0.4, "z1": 0.4, "x2": 0.6, "y2": 0.6, "z2": 0.6 }],
        "controls": [{ "id": "dial", "box": { "x1": 0.4, "y1": 0.4, "z1": 0.4, "x2": 0.6, "y2": 0.6, "z2": 0.6 } }]
      },
      {
        "x": 6, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
        "controls": [{ "id": "knob", "box": { "x1": 0, "y1": 0.5, "z1": 0, "x2": 0.25, "y2": 0.75, "z2": 0.25 } }]
      },
      {
        "x": 8, "y": 0, "z": 0,
        "collisionBoxes": [{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
        "selectionBoxes": []
      },
      {
        "x": 0, "y": 1, "z": 0,
        "collisionBoxes": [
          { "x1": 0.0, "y1": 0.25, "z1": 0.25, "x2": 0.375, "y2": 0.75, "z2": 0.75 },
          { "x1": 0.125, "y1": 0.0, "z1": 0.125, "x2": 0.8125, "y2": 0.25, "z2": 0.875 }
        ],
        "selectionBoxes": [
          { "x1": 0.0, "y1": 0.0, "z1": 0.25, "x2": 0.375, "y2": 0.75, "z2": 0.75 },
          { "x1": 0.3125, "y1": 0.0, "z1": 0.0, "x2": 0.6875, "y2": 1.0, "z2": 1.0 },
          { "x1": 0.6875, "y1": 0.0, "z1": 0.375, "x2": 1.0, "y2": 0.3125, "z2": 0.6875 }
        ],
        "controls": [
          { "id": "counter", "box": { "x1": 0.6875, "y1": 0.0, "z1": 0.375, "x2": 1.0, "y2": 0.3125, "z2": 0.6875 } }
        ],
        "doors": [{
          "control": "counterlid",
          "opening": [{ "x1": 0.875, "y1": 0.0, "z1": 0.375, "x2": 1.0, "y2": 1.0, "z2": 0.625 }],
          "open": [{ "x1": 0.9375, "y1": 0.1875, "z1": 0.4375, "x2": 1.0, "y2": 0.25, "z2": 0.5625 }],
          "shut": [{ "x1": 0.75, "y1": 0.125, "z1": 0.375, "x2": 1.0, "y2": 0.375, "z2": 0.625 }],
          "openCollides": false
        }]
      }
    ]
    """;

  #region Selection and collision

#if GAME_GE_1_22
  // Fails when GetSelectionBoxes returns the collision boxes.
  [Fact]
  public void Selection_differs_from_collision()
  {
    var scene = new Scene();
    BlockPos pos = scene.Cell(1, 0, 0);

    Cuboidf[] collision = scene.Filler.GetCollisionBoxes(scene.Accessor, pos);
    Cuboidf[] selection = scene.Filler.GetSelectionBoxes(scene.Accessor, pos);

    AssertBox(PanelCollision, Assert.Single(collision));
    Assert.Equal(2, selection.Length);
    AssertBox(PanelValve, selection[0]);
    AssertBox(PanelBody, selection[1]);
  }

  // Fails when Compose returns plain Cuboidf for the control boxes.
  [Fact]
  public void Control_boxes_carry_their_id_at_every_facing()
  {
    foreach (int angle in new[] { 0, 90, 180, 270 })
    {
      var scene = new Scene(angle);
      BlockPos pos = scene.Cell(1, 0, 0);

      Cuboidf[] selection = scene.Filler.GetSelectionBoxes(scene.Accessor, pos);

      var valve = Assert.IsType<CuboidfWithId>(selection[0]);
      Assert.Equal("valve", valve.Id);
      AssertBox(ExOrientation.RotateBoxes([PanelValve], angle)[0], valve);
      Assert.IsType<Cuboidf>(selection[1]);
      AssertBox(ExOrientation.RotateBoxes([PanelBody], angle)[0], selection[1]);

      Cuboidf[] appended = scene.Filler.GetSelectionBoxes(
        scene.Accessor,
        scene.Cell(6, 0, 0)
      );
      var knob = Assert.IsType<CuboidfWithId>(appended[0]);
      Assert.Equal("knob", knob.Id);
      AssertBox(
        ExOrientation.RotateBoxes(
          [new Cuboidf(0f, 0.5f, 0f, 0.25f, 0.75f, 0.25f)],
          angle
        )[0],
        knob
      );
    }
  }
#endif

  // Fails when the row is looked up at RotateOffset by +angle (wrong row at 90 and 270).
  [Fact]
  public void The_row_is_found_at_every_facing() {
    foreach (int angle in new[] { 0, 90, 180, 270 }) {
      var scene = new Scene(angle);

      AssertBox(
        ExOrientation.RotateBoxes([PanelCollision], angle)[0],
        Assert.Single(
          scene.Filler.GetCollisionBoxes(scene.Accessor, scene.Cell(1, 0, 0))
        )
      );
      AssertBox(
        ExOrientation.RotateBoxes([MirrorCollision], angle)[0],
        Assert.Single(
          scene.Filler.GetCollisionBoxes(scene.Accessor, scene.Cell(-1, 0, 0))
        )
      );
    }
  }

  // Fails when the saved boxes are read before the definition's row.
  [Fact]
  public void The_def_outranks_the_saved_boxes() {
    var scene = new Scene();
    BlockPos pos = scene.Cell(
      1,
      0,
      0,
      saved: [new Cuboidf(0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f)]
    );

    AssertBox(
      PanelCollision,
      Assert.Single(scene.Filler.GetCollisionBoxes(scene.Accessor, pos))
    );
  }

  // Fails when a cell with no principal row answers the base full cube instead of its saved boxes.
  [Fact]
  public void A_missing_principal_falls_back_to_saved_boxes() {
    Cuboidf saved = new(0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f);
    var scene = new Scene(principalGone: true);
    BlockPos orphan = scene.Cell(1, 0, 0, saved: [saved]);
    var live = new Scene();
    BlockPos noRow = live.Cell(7, 0, 0, saved: [saved]);

    AssertBox(
      saved,
      Assert.Single(scene.Filler.GetCollisionBoxes(scene.Accessor, orphan))
    );
    AssertBox(
      saved,
      Assert.Single(live.Filler.GetCollisionBoxes(live.Accessor, noRow))
    );
  }

  // Fails when Compose reads an empty collision as the full cube.
  [Fact]
  public void Empty_collision_has_none() {
    var scene = new Scene();
    BlockPos pos = scene.Cell(2, 0, 0);

    Assert.Empty(scene.Filler.GetCollisionBoxes(scene.Accessor, pos));
#if GAME_GE_1_22
    Assert.Empty(scene.Filler.GetSelectionBoxes(scene.Accessor, pos));
#endif
  }

  #endregion

  #region Doors

  // Fails when the shut leaves are left out of collision.
  [Fact]
  public void A_shut_door_collides_and_selects() {
    var scene = new Scene();
    BlockPos pos = scene.Cell(3, 0, 0);

    Cuboidf[] collision = scene.Filler.GetCollisionBoxes(scene.Accessor, pos);

    Assert.Equal(2, collision.Length);
    AssertBox(DoorShut, collision[0]);
    AssertBox(DoorBody, collision[1]);
#if GAME_GE_1_22
    Cuboidf[] selection = scene.Filler.GetSelectionBoxes(scene.Accessor, pos);
    var leaf = Assert.IsType<CuboidfWithId>(selection[0]);
    Assert.Equal("hatch", leaf.Id);
    AssertBox(DoorShut, leaf);
#endif
  }

  // Fails when the door state is ignored (Mutant A: always shut), and when open leaves collide whatever
  // openCollides says (Mutant B).
  [Fact]
  public void An_open_door_selects_its_open_leaf() {
    var scene = new Scene();
    scene.Principal.States["hatch"] = ControlState.Open;
    BlockPos colliding = scene.Cell(3, 0, 0);
    BlockPos passable = scene.Cell(4, 0, 0);

    Cuboidf[] open = scene.Filler.GetCollisionBoxes(scene.Accessor, colliding);
    Assert.Equal(2, open.Length);
    AssertBox(DoorOpen, open[0]);
    AssertBox(
      DoorBody,
      Assert.Single(scene.Filler.GetCollisionBoxes(scene.Accessor, passable))
    );
#if GAME_GE_1_22
    Cuboidf[] selection = scene.Filler.GetSelectionBoxes(
      scene.Accessor,
      passable
    );
    Assert.Equal(2, selection.Length);
    var leaf = Assert.IsType<CuboidfWithId>(selection[0]);
    Assert.Equal("hatch", leaf.Id);
    AssertBox(DoorOpen, leaf);
    AssertBox(DoorBody, selection[1]);
#endif
  }

  #endregion

  #region Control states

  // Fails when Hidden is read as Shown.
  [Fact]
  public void Hidden_removes_the_control() {
    var scene = new Scene();
    scene.Principal.States["hatch"] = ControlState.Hidden;
    scene.Principal.States["valve"] = ControlState.Hidden;
    BlockPos door = scene.Cell(3, 0, 0);
    BlockPos panel = scene.Cell(1, 0, 0);

    AssertBox(
      DoorBody,
      Assert.Single(scene.Filler.GetCollisionBoxes(scene.Accessor, door))
    );
#if GAME_GE_1_22
    AssertBox(
      DoorBody,
      Assert.Single(scene.Filler.GetSelectionBoxes(scene.Accessor, door))
    );
    AssertBox(
      PanelBody,
      Assert.Single(scene.Filler.GetSelectionBoxes(scene.Accessor, panel))
    );
#endif
  }

#if GAME_GE_1_22
  // Fails when a cell left with no selection by a Hidden control keeps the empty selection.
  [Fact]
  public void A_hidden_control_alone_in_its_cell_selects_the_body()
  {
    var scene = new Scene();
    BlockPos pos = scene.Cell(5, 0, 0);
    Assert.Equal(
      "dial",
      Assert
        .IsType<CuboidfWithId>(
          Assert.Single(scene.Filler.GetSelectionBoxes(scene.Accessor, pos))
        )
        .Id
    );

    scene.Principal.States["dial"] = ControlState.Hidden;

    AssertBox(
      Full,
      Assert.Single(scene.Filler.GetSelectionBoxes(scene.Accessor, pos))
    );
  }
#endif

#if GAME_GE_1_22
  // Fails when the empty-selection fallback ignores whether a control was hidden.
  [Fact]
  public void An_explicit_empty_selection_leaves_the_cell_unselectable() {
    var scene = new Scene();
    BlockPos pos = scene.Cell(8, 0, 0);

    Assert.Empty(scene.Filler.GetSelectionBoxes(scene.Accessor, pos));
    AssertBox(
      Full,
      Assert.Single(scene.Filler.GetCollisionBoxes(scene.Accessor, pos))
    );
  }
#endif

  // Fails when only the principal's block entity is asked and not its behaviours.
  [Fact]
  public void A_behaviour_answers_the_state() {
    var scene = new Scene();
    var behaviour = new StatesBehavior(scene.Principal);
    scene.Principal.Behaviors.Add(behaviour);
    behaviour.States["hatch"] = ControlState.Open;

    Assert.Equal(
      ControlState.Open,
      ControlBoxes.StateOf(scene.Principal, "hatch")
    );
    Assert.Equal(
      ControlState.Shown,
      ControlBoxes.StateOf(scene.Principal, "valve")
    );
    Assert.Equal(ControlState.Shown, ControlBoxes.StateOf(null, "hatch"));
    AssertBox(
      DoorOpen,
      scene.Filler.GetCollisionBoxes(scene.Accessor, scene.Cell(3, 0, 0))[0]
    );
  }

  // Fails when Stage is ignored.
  [Fact]
  public void A_control_is_hidden_below_its_stage() {
    var scene = new Scene(
      controls: """[{ "id": "valve", "kind": "valve", "stage": 3 }]"""
    );
    ExRightClickConstructable rcc = scene.AddConstruction(stage: 2);
    BlockPos pos = scene.Cell(1, 0, 0);

    Assert.Equal(
      ControlState.Hidden,
      ControlBoxes.StateOf(scene.Principal, "valve")
    );
    Assert.Null(rcc.StateOf("other"));
#if GAME_GE_1_22
    AssertBox(
      PanelBody,
      Assert.Single(scene.Filler.GetSelectionBoxes(scene.Accessor, pos))
    );
#endif

    scene.SetStage(rcc, 3);

    Assert.Equal(
      ControlState.Shown,
      ControlBoxes.StateOf(scene.Principal, "valve")
    );
#if GAME_GE_1_22
    Assert.Equal(2, scene.Filler.GetSelectionBoxes(scene.Accessor, pos).Length);
#endif
  }

  // Fails when the first non-null answer wins over a later Hidden.
  [Fact]
  public void Hidden_outranks_another_behaviours_answer() {
    foreach (bool counterFirst in new[] { true, false }) {
      var scene = new Scene(
        controls: """[{ "id": "valve", "kind": "valve", "stage": 3 }]"""
      );
      var counter = new StatesBehavior(scene.Principal);
      counter.States["valve"] = ControlState.Open;
      if (counterFirst)
        scene.Principal.Behaviors.Add(counter);
      scene.AddConstruction(stage: 2);
      if (!counterFirst)
        scene.Principal.Behaviors.Add(counter);

      Assert.Equal(
        ControlState.Hidden,
        ControlBoxes.StateOf(scene.Principal, "valve")
      );
    }
  }

  #endregion

#if GAME_GE_1_22
  // Fails when the door leaves are appended after the control boxes.
  [Fact]
  public void A_lid_leaf_wins_a_tie_over_the_counter_box()
  {
    var scene = new Scene();
    BlockPos pos = scene.Cell(0, 1, 0);
    var air = new Block { SideSolid = new SmallBoolArray(0) };
    var supplier = Substitute.For<IWorldIntersectionSupplier>();
    supplier.blockAccessor.Returns(scene.Accessor);
    scene
      .Accessor.GetBlock(Arg.Any<BlockPos>(), BlockLayersAccess.Fluid)
      .Returns(air);
    supplier.GetBlock(Arg.Any<BlockPos>()).Returns(scene.Filler);
    supplier
      .GetBlockIntersectionBoxes(Arg.Any<BlockPos>())
      .Returns(call =>
        scene.Filler.GetSelectionBoxes(scene.Accessor, call.Arg<BlockPos>())
      );
    var test = new AABBIntersectionTest(supplier);
    test.ray.origin.Set(pos.X + 2.0, pos.Y + 0.2, pos.Z + 0.5);
    test.ray.dir.Set(-1, 0, 0);

    bool hit = test.RayIntersectsBlockSelectionBox(pos.Copy(), null);

    Assert.True(hit);
    Assert.Equal("counterlid", test.hitOnSelectionBoxId);
  }
#endif

  #region Saves

  // Fails when the composed selection boxes are written to the save tree.
  [Fact]
  public void Filler_saves_do_not_grow() {
    var scene = new Scene();
    BlockPos pos = scene.Cell(1, 0, 0, saved: [PanelCollision]);
    var be = (BlockEntityStructureFiller)scene.Accessor.GetBlockEntity(pos);
    scene.Filler.GetSelectionBoxes(scene.Accessor, pos);

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    Assert.Equal(
      ["allowAttach", "cboxes", "cx", "cy", "cz"],
      tree.Keys.Where(k => !k.StartsWith("pos") && k != "blockCode")
        .OrderBy(k => k)
    );
  }

  #endregion

  #region Helpers

  private static void AssertBox(Cuboidf expected, Cuboidf actual) {
    Assert.Equal(expected.X1, actual.X1, 4);
    Assert.Equal(expected.Y1, actual.Y1, 4);
    Assert.Equal(expected.Z1, actual.Z1, 4);
    Assert.Equal(expected.X2, actual.X2, 4);
    Assert.Equal(expected.Y2, actual.Y2, 4);
    Assert.Equal(expected.Z2, actual.Z2, 4);
  }

  private sealed class Scene {
    public readonly BlockStructureFiller Filler;
    public readonly StatesEntity Principal;
    public readonly IBlockAccessor Accessor = Substitute.For<IBlockAccessor>();
    private readonly int _angle;

    public Scene(
      int angle = 0,
      bool principalGone = false,
      string? controls = null
    ) {
      _angle = angle;
      var attributes = new JObject {
        ["fillerOffsets"] = JArray.Parse(Offsets),
      };
      if (controls != null)
        attributes["controls"] = JArray.Parse(controls);
      var host = TestBlocks.Configure(
        new HostBlock(),
        "test:host",
        50,
        ("side", ExOrientation.SideFromAngle(angle))
      );
      host.Attributes = new JsonObject(attributes);
      Filler = TestBlocks.Configure(
        new BlockStructureFiller(),
        "exlib:structurefiller",
        70
      );
      Filler.CollisionBoxes = [Full];
      Filler.SelectionBoxes = [Full];
      Principal = new StatesEntity { Pos = PrincipalPos, Block = host };
      Accessor
        .GetBlock(PrincipalPos)
        .Returns(principalGone ? new Block() : host);
      Accessor.GetBlockEntity(PrincipalPos).Returns(Principal);
    }

    public BlockPos Cell(int x, int y, int z, Cuboidf[]? saved = null) {
      Vec3i r = ExOrientation.RotateOffset(x, y, z, _angle);
      BlockPos pos = PrincipalPos.AddCopy(r.X, r.Y, r.Z);
      Accessor
        .GetBlockEntity(pos)
        .Returns(
          new BlockEntityStructureFiller {
            Pos = pos,
            Block = Filler,
            Principal = PrincipalPos.Copy(),
            CollisionBoxes = saved,
          }
        );
      return pos;
    }

    public ExRightClickConstructable AddConstruction(int stage) {
      var rcc = new ExRightClickConstructable(Principal);
      Principal.Behaviors.Add(rcc);
      SetStage(rcc, stage);
      return rcc;
    }

    public void SetStage(ExRightClickConstructable rcc, int stage) =>
      ReflectionHelpers.SetField(
        rcc,
        "rcc",
        new ExRightClickConstruction {
          Stages =
          [
            .. Enumerable.Range(0, 4).Select(_ => new ExConstructionStage()),
          ],
          CurrentCompletedStage = stage,
        }
      );
  }

  private sealed class HostBlock : Block, IFillerHost {
    public JsonObject? FillerOffsets => Attributes?["fillerOffsets"];
  }

  private sealed class StatesEntity : BlockEntity, IControlStates {
    public readonly Dictionary<string, ControlState> States = [];

    public ControlState? StateOf(string controlId) =>
      States.TryGetValue(controlId, out ControlState state) ? state : null;
  }

  private sealed class StatesBehavior(BlockEntity be)
    : BlockEntityBehavior(be),
      IControlStates {
    public readonly Dictionary<string, ControlState> States = [];

    public ControlState? StateOf(string controlId) =>
      States.TryGetValue(controlId, out ControlState state) ? state : null;
  }

  #endregion
}
