using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A cell's boxes as data: the plan's box keys read from a row, written back from a spec,
/// turned by the placed angle, and the block-level control table.</summary>
public class CellBoxesTests {
  // The twin-tub plan's (0,1,0) cell.
  private const string TwinTubCell = """
    {
      "x": 0, "y": 1, "z": 0, "allowAttach": true,
      "collisionBoxes": [
        { "x1": 0.0, "y1": 0.25, "z1": 0.25, "x2": 0.375, "y2": 0.75, "z2": 0.75 },
        { "x1": 0.125, "y1": 0.0, "z1": 0.125, "x2": 0.8125, "y2": 0.25, "z2": 0.875 }
      ],
      "selectionBoxes": [
        { "x1": 0.0, "y1": 0.0, "z1": 0.25, "x2": 0.375, "y2": 0.75, "z2": 0.75 },
        { "x1": 0.3125, "y1": 0.0, "z1": 0.0, "x2": 0.6875, "y2": 1.0, "z2": 1.0 },
        { "x1": 0.625, "y1": 0.0, "z1": 0.25, "x2": 1.0, "y2": 0.375, "z2": 0.375 },
        { "x1": 0.6875, "y1": 0.0, "z1": 0.6875, "x2": 0.8125, "y2": 0.3125, "z2": 0.75 },
        { "x1": 0.6875, "y1": 0.3125, "z1": 0.25, "x2": 0.8125, "y2": 0.6875, "z2": 0.75 },
        { "x1": 0.6875, "y1": 0.6875, "z1": 0.3125, "x2": 0.8125, "y2": 0.75, "z2": 0.6875 },
        { "x1": 0.6875, "y1": 0.0, "z1": 0.375, "x2": 1.0, "y2": 0.3125, "z2": 0.6875 }
      ],
      "controls": [
        { "id": "counter", "box": { "x1": 0.6875, "y1": 0.0, "z1": 0.375, "x2": 1.0, "y2": 0.3125, "z2": 0.6875 } }
      ],
      "doors": [
        {
          "control": "counterlid",
          "opening": [{ "x1": 0.875, "y1": 0.0, "z1": 0.375, "x2": 1.0, "y2": 1.0, "z2": 0.625 }],
          "open": [{ "x1": 0.9375, "y1": 0.1875, "z1": 0.4375, "x2": 1.0, "y2": 0.25, "z2": 0.5625 }],
          "shut": [{ "x1": 0.75, "y1": 0.125, "z1": 0.375, "x2": 1.0, "y2": 0.375, "z2": 0.625 }],
          "openCollides": false
        }
      ]
    }
    """;

  private const string Box =
    """{ "x1": 0, "y1": 0, "z1": 0, "x2": 0.5, "y2": 0.5, "z2": 0.5 }""";
  private const string Other =
    """{ "x1": 0.5, "y1": 0, "z1": 0, "x2": 1, "y2": 0.5, "z2": 0.5 }""";

  #region Read

  // Fails when Read skips `doors`.
  [Fact]
  public void Read_takes_the_plan_cell_keys() {
    CellBoxes boxes = ReadRow(TwinTubCell)!;

    Assert.Equal(2, boxes.Collision!.Length);
    Assert.Equal(7, boxes.Selection!.Length);
    var control = Assert.Single(boxes.Controls!);
    Assert.Equal("counter", control.Id);
    Assert.Same(boxes.Selection[6], control.Box);
    var door = Assert.Single(boxes.Doors!);
    Assert.Equal("counterlid", door.Control);
    Assert.False(door.OpenCollides);
    AssertBox(
      new Cuboidf(0.75f, 0.125f, 0.375f, 1f, 0.375f, 0.625f),
      Assert.Single(door.Shut)
    );
    AssertBox(
      new Cuboidf(0.9375f, 0.1875f, 0.4375f, 1f, 0.25f, 0.5625f),
      Assert.Single(door.Open)
    );
    AssertBox(
      new Cuboidf(0.875f, 0f, 0.375f, 1f, 1f, 0.625f),
      Assert.Single(door.Opening)
    );
#if GAME_GE_1_22
    Assert.Equal("counter", ((CuboidfWithId)boxes.Selection[6]).Id);
    Assert.All(
      boxes.Selection.Take(6),
      b => Assert.IsNotType<CuboidfWithId>(b)
    );
#endif
  }

  // Fails when Read returns a cell for a row with none of the box keys.
  [Fact]
  public void A_row_without_box_keys_reads_as_null() {
    Assert.Null(ReadRow("""{ "x": 1, "y": 0, "z": 0, "allowAttach": true }"""));
  }

  // Fails when Read turns an explicit empty `selectionBoxes` into null.
  [Fact]
  public void An_empty_selection_reads_as_an_empty_array() {
    CellBoxes boxes = ReadRow($$"""{ "collisionBoxes": [{{Box}}], "selectionBoxes": [] }""")!;

    Assert.NotNull(boxes.Selection);
    Assert.Empty(boxes.Selection);
  }

  // Fails when control boxes are always appended (8 boxes).
  [Fact]
  public void A_control_box_names_its_equal_selection_box() {
    CellBoxes boxes = ReadRow(
      $$"""{ "selectionBoxes": [{{Box}}, {{Other}}], "controls": [{ "id": "a", "box": {{Other}} }] }"""
    )!;

    Assert.Equal(2, boxes.Selection!.Length);
    Assert.Same(boxes.Selection[1], boxes.Controls![0].Box);
#if GAME_GE_1_22
    Assert.Equal("a", ((CuboidfWithId)boxes.Selection[1]).Id);
#endif
  }

  // Fails when a control box that equals no selection entry is dropped.
  [Fact]
  public void A_control_box_outside_the_selection_is_appended() {
    CellBoxes boxes = ReadRow(
      $$"""{ "selectionBoxes": [{{Box}}], "controls": [{ "id": "a", "box": {{Other}} }] }"""
    )!;

    Assert.Equal(2, boxes.Selection!.Length);
    AssertBox(new Cuboidf(0.5f, 0f, 0f, 1f, 0.5f, 0.5f), boxes.Selection[1]);
    Assert.Same(boxes.Selection[1], boxes.Controls![0].Box);
#if GAME_GE_1_22
    Assert.Equal("a", ((CuboidfWithId)boxes.Selection[1]).Id);
#endif
  }

  // Fails when StructureFillers.ReadBoxes returns null for a zero-length `collisionBoxes`.
  [Fact]
  public void Empty_collision_reads_as_none_and_absent_as_full() {
    var offsets = StructureFillers.ReadOffsets(
      new JsonObject(
        JArray.Parse(
          """[{ "x": 1, "y": 0, "z": 0, "collisionBoxes": [] }, { "x": 2, "y": 0, "z": 0 }]"""
        )
      )
    );

    Assert.Empty(offsets[0].CollisionBoxes!);
    Assert.Empty(offsets[0].Boxes!.Collision!);
    Assert.Null(offsets[1].CollisionBoxes);
    Assert.Null(offsets[1].Boxes);
  }

  #endregion

  #region Turned

  // Fails when Turned drops the ids (boxes turned through RotatedCopy and rebuilt without them).
  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void Turned_keeps_ids_and_leaves_at_every_angle(int angle) {
    CellBoxes boxes = ReadRow(TwinTubCell)!;

    CellBoxes turned = boxes.Turned(angle);

    AssertBoxes(
      ExOrientation.RotateBoxes(boxes.Collision!, angle),
      turned.Collision!
    );
    AssertBoxes(
      ExOrientation.RotateBoxes(boxes.Selection!, angle),
      turned.Selection!
    );
    AssertBoxes(
      ExOrientation.RotateBoxes(boxes.Doors![0].Shut, angle),
      turned.Doors![0].Shut
    );
    AssertBoxes(
      ExOrientation.RotateBoxes(boxes.Doors[0].Open, angle),
      turned.Doors[0].Open
    );
    Assert.Equal("counter", Assert.Single(turned.Controls!).Id);
    Assert.Same(turned.Selection![6], turned.Controls![0].Box);
    Assert.False(turned.Doors[0].OpenCollides);
#if GAME_GE_1_22
    Assert.Equal("counter", ((CuboidfWithId)turned.Selection[6]).Id);
#endif
  }

  // Fails when Turned(Func) skips the doors or the selection boxes, or drops the control id.
  [Fact]
  public void Turned_by_function_applies_it_to_every_box() {
    CellBoxes boxes = ReadRow(TwinTubCell)!;

    CellBoxes lifted = boxes.Turned(b => new Cuboidf(
      b.X1,
      b.Y1 + 1,
      b.Z1,
      b.X2,
      b.Y2 + 1,
      b.Z2
    ));

    Assert.Equal(boxes.Selection![6].Y1 + 1, lifted.Selection![6].Y1, 4);
    Assert.Equal(
      boxes.Doors![0].Shut[0].Y2 + 1,
      lifted.Doors![0].Shut[0].Y2,
      4
    );
#if GAME_GE_1_22
    Assert.Equal("counter", ((CuboidfWithId)lifted.Selection[6]).Id);
#endif
  }

  #endregion

  #region Spec and def

  // Fails when SerializeFillerCells skips `selectionBoxes`.
  [Fact]
  public void A_spec_round_trips_through_the_def() {
    var shut = new Cuboidf(0.75f, 0.125f, 0.375f, 1f, 0.375f, 0.625f);
    var counter = new Cuboidf(0.5f, 0f, 0f, 1f, 0.5f, 0.5f);
    var spec = new FillerCellSpec(
      1,
      0,
      0,
      CollisionBoxes: [new Cuboidf(0f, 0f, 0f, 1f, 0.5f, 1f)],
      SelectionBoxes: [new Cuboidf(0f, 0f, 0f, 0.5f, 0.5f, 0.5f), counter],
      Controls: [new ControlBox("counter", counter)],
      Doors: [new DoorLeaf("lid", [shut], [], [shut], true)]
    );

    JObject json = ExBlockDef.Create("d", "c").FillerOffsets([spec]).ToJson();
    var offsets = StructureFillers.ReadOffsets(
      new JsonObject(json["attributes"]!["fillerOffsets"]!)
    );

    CellBoxes boxes = Assert.Single(offsets).Boxes!;
    Assert.Single(boxes.Collision!);
    Assert.Equal(2, boxes.Selection!.Length);
    AssertBox(counter, boxes.Selection[1]);
    Assert.Equal("counter", Assert.Single(boxes.Controls!).Id);
    var door = Assert.Single(boxes.Doors!);
    Assert.Equal("lid", door.Control);
    Assert.True(door.OpenCollides);
    AssertBox(shut, Assert.Single(door.Shut));
    Assert.Empty(door.Open);
  }

  // Fails when every cell writes `"controls": []`.
  [Fact]
  public void Old_specs_serialise_unchanged() {
    JObject json = ExBlockDef
      .Create("d", "c")
      .FillerOffsets([
        new FillerCellSpec(1, 0, 0, AllowAttach: true),
        new FillerCellSpec(
          0,
          1,
          0,
          CollisionBoxes: [new Cuboidf(0f, 0f, 0f, 1f, 0.5f, 1f)],
          PortFace: "west",
          PortNetworkType: "mpenergy"
        ),
      ])
      .ToJson();

    Assert.Equal(
      """[{"x":1,"y":0,"z":0,"allowAttach":true},{"x":0,"y":1,"z":0,"collisionBox":{"x1":0.0,"y1":0.0,"z1":0.0,"x2":1.0,"y2":0.5,"z2":1.0},"portFace":"west","portNetwork":"mpenergy"}]""",
      json["attributes"]!["fillerOffsets"]!.ToString(
        Newtonsoft.Json.Formatting.None
      )
    );
  }

  // Fails when an empty collision list is written as no `collisionBoxes` key.
  [Fact]
  public void An_empty_collision_list_serialises_as_an_empty_array() {
    JObject json = ExBlockDef
      .Create("d", "c")
      .FillerOffsets([new FillerCellSpec(1, 0, 0, CollisionBoxes: [])])
      .ToJson();

    JToken row = json["attributes"]!["fillerOffsets"]![0]!;
    Assert.Empty((JArray)row["collisionBoxes"]!);
  }

  // Fails when Validate lets an empty control id through.
  [Fact]
  public void Validate_refuses_an_empty_control_or_door_id() {
    var box = new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f);

    Assert.Throws<ArgumentException>(() =>
      StructureFootprint.Validate([
        new FillerCellSpec(1, 0, 0, Controls: [new ControlBox("", box)]),
      ])
    );
    Assert.Throws<ArgumentException>(() =>
      StructureFootprint.Validate([
        new FillerCellSpec(
          1,
          0,
          0,
          Doors: [new DoorLeaf("", [], [], [], false)]
        ),
      ])
    );
  }

  // Fails when WithBoxes copies fewer than the four members onto the cell, or changes a cell no row names.
  [Fact]
  public void WithBoxes_sets_the_four_members_by_offset() {
    var counter = new Cuboidf(0.5f, 0f, 0f, 1f, 0.5f, 0.5f);
    IReadOnlyList<FillerCellSpec> cells = StructureFootprint.Layout(f =>
      f.Slice(0, "0##")
    );

    var withBoxes = cells.WithBoxes(
      new Dictionary<(int, int, int), CellBoxes> {
        [(0, 0, 1)] = new CellBoxes(
          [],
          [counter],
          [new ControlBox("c", counter)],
          [new DoorLeaf("d", [], [], [], false)]
        ),
      }
    );

    Assert.Empty(withBoxes[0].CollisionBoxes!);
    Assert.Single(withBoxes[0].SelectionBoxes!);
    Assert.Equal("c", Assert.Single(withBoxes[0].Controls!).Id);
    Assert.Equal("d", Assert.Single(withBoxes[0].Doors!).Control);
    Assert.Equal(cells[1], withBoxes[1]);
  }

  // Fails when WithBoxes skips an offset no cell has.
  [Fact]
  public void WithBoxes_refuses_an_offset_with_no_cell() {
    IReadOnlyList<FillerCellSpec> cells = StructureFootprint.Layout(f =>
      f.Slice(0, "0##")
    );

    var ex = Assert.Throws<ArgumentException>(() =>
      cells.WithBoxes(
        new Dictionary<(int, int, int), CellBoxes> {
          [(5, 0, 0)] = new CellBoxes(),
        }
      )
    );
    Assert.Contains("(5,0,0)", ex.Message);
  }

  // Fails when PrincipalCellByType writes under `attributes`.
  [Fact]
  public void ByType_writes_under_attributesByType() {
    var counter = new Cuboidf(0.5f, 0f, 0f, 1f, 0.5f, 0.5f);
    JObject json = ExBlockDef
      .Create("d", "c")
      .PrincipalCellByType(
        "c-plain",
        new CellBoxes([], [counter], [new ControlBox("c", counter)])
      )
      .ControlsByType("c-plain", new BlockControl("c", "instrument", [], 2))
      .ToJson();

    Assert.Null(json["attributes"]);
    JToken type = json["attributesByType"]!["c-plain"]!;
    Assert.Equal("c", (string?)type["principalCell"]!["controls"]![0]!["id"]);
    Assert.Empty((JArray)type["principalCell"]!["collisionBoxes"]!);
    Assert.Equal(2, (int?)type["controls"]![0]!["stage"]);
  }

  // Fails when PrincipalCell or Controls write outside `attributes`, or leave out a control's kind.
  [Fact]
  public void The_principal_cell_and_controls_write_under_attributes() {
    JObject json = ExBlockDef
      .Create("d", "c")
      .PrincipalCell(new CellBoxes([new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)]))
      .Controls(new BlockControl("c", "valve", ["d"]))
      .ToJson();

    Assert.NotNull(json["attributes"]!["principalCell"]!["collisionBox"]);
    Assert.Equal(
      "valve",
      (string?)json["attributes"]!["controls"]![0]!["kind"]
    );
  }

  #endregion

  #region Block table

  // Fails when the alsoIds are read in reverse, or a row's `stage` is ignored.
  [Fact]
  public void AlsoIds_read_in_table_order() {
    Block block = TestBlocks.Configure(new Block(), "exlib:t", 1);
    block.Attributes = new JsonObject(
      JToken.Parse(
        """
        { "controls": [
          { "id": "hud", "kind": "instrument", "alsoIds": ["regulator", "gauge"], "stage": 3 },
          { "id": "counter", "kind": "instrument", "alsoIds": [] }
        ] }
        """
      )
    );

    var table = BlockControls.Of(block);

    Assert.Equal(2, table.Count);
    Assert.Equal(["regulator", "gauge"], table[0].AlsoIds);
    Assert.Equal(3, table[0].Stage);
    Assert.Equal(0, BlockControls.Find(block, "counter")!.Stage);
    Assert.Null(BlockControls.Find(block, "none"));
    Assert.Same(table, BlockControls.Of(block));
  }

  // Fails when BlockControls.Of returns a row for a block without a `controls` table.
  [Fact]
  public void A_block_without_the_table_has_no_controls() {
    Block block = TestBlocks.Configure(new Block(), "exlib:t", 2);
    Assert.Empty(BlockControls.Of(block));
  }

  #endregion

  #region Helpers

  private static CellBoxes? ReadRow(string json) =>
    CellBoxes.Read(new JsonObject(JToken.Parse(json)));

  private static void AssertBoxes(Cuboidf[] expected, Cuboidf[] actual) {
    Assert.Equal(expected.Length, actual.Length);
    for (int i = 0; i < expected.Length; i++)
      AssertBox(expected[i], actual[i]);
  }

  private static void AssertBox(Cuboidf expected, Cuboidf actual) {
    Assert.Equal(expected.X1, actual.X1, 4);
    Assert.Equal(expected.Y1, actual.Y1, 4);
    Assert.Equal(expected.Z1, actual.Z1, 4);
    Assert.Equal(expected.X2, actual.X2, 4);
    Assert.Equal(expected.Y2, actual.Y2, 4);
    Assert.Equal(expected.Z2, actual.Z2, 4);
  }

  #endregion
}
