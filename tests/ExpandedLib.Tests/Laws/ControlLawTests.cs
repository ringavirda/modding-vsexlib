#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The control law against small machines that each break one rule of a control declaration, and
/// one that declares every awkward shape the law must leave alone.</summary>
public class ControlLawTests {
  private static readonly System.Reflection.Assembly[] Exlib =
  [
    typeof(BlockStructureFiller).Assembly,
  ];

  private static readonly Cuboidf Slab = new(0f, 0f, 0f, 1f, 0.5f, 1f);
  private static readonly Cuboidf HalfSlab = new(0f, 0f, 0f, 0.5f, 0.5f, 1f);
  private static readonly Cuboidf Dial = new(0.25f, 0.5f, 0.25f, 0.75f, 1f, 0.75f);
  private static readonly Cuboidf Gauge = new(0f, 0.5f, 0f, 0.2f, 1f, 0.2f);
  private static readonly Cuboidf Leaf = new(0.2f, 0.5f, 0.2f, 0.8f, 1f, 0.8f);
  private static readonly Cuboidf Ajar = new(0f, 0.5f, 0f, 0.1f, 1f, 1f);

  private static void Prepare(TestWorld world) =>
    world.RegisterClass("test-stripsids", typeof(StripsIds));

  private static BlockLaws.Law Run(params ExBlockDef[] defs) =>
    ControlLaw.Run(BlockLaws.Stand(defs, Exlib, Prepare), "test");

  private static JArray Table(params (string Id, string Kind, string[] Also)[] rows) =>
    new(
      rows.Select(r => new JObject {
        ["id"] = r.Id,
        ["kind"] = r.Kind,
        ["alsoIds"] = new JArray(r.Also),
        ["stage"] = 0,
      })
    );

  private static ExBlockDef Machine(
    string code,
    CellBoxes? cell = null,
    JArray? table = null,
    CellBoxes? principal = null,
    string cls = "ExFilledMegastructure"
  ) {
    ExBlockDef def = ExBlockDef
      .Create("test", code)
      .Class(cls)
      .SideVariant()
      .FillerOffsets([
        new FillerCellSpec(
          1,
          0,
          0,
          CollisionBoxes: cell?.Collision,
          SelectionBoxes: cell?.Selection,
          Controls: cell?.Controls,
          Doors: cell?.Doors
        ),
      ]);
    if (table != null)
      def.Attribute("controls", table);
    if (principal != null) {
      var row = new JObject();
      principal.WriteTo(row);
      def.Attribute("principalCell", row);
    }
    return def;
  }

  private static CellBoxes DialCell() =>
    new(
      [Slab],
      [Slab, Dial],
      [new ControlBox("dial", Dial)]
    );

  private static DoorLeaf Hatch(string control) =>
    new(control, [Leaf], [Ajar], [], false);

  private static string[] Sides(string code, string message) =>
    [
      .. new[] { "e", "n", "s", "w" }.Select(side => $"test:{code}-{side} {message}"),
    ];

  private static void AssertFindings(string[] expected, BlockLaws.Law law) {
    Assert.Equal(1, law.Blocks);
    Assert.Equal(4, law.Cases);
    Assert.Equal(expected, law.Findings.Order(StringComparer.Ordinal));
  }

  // Fails when the law stops reading a cell's control ids, or a door's control, against the controls table.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void A_cell_or_door_naming_a_control_the_table_lacks_is_named() {
    var doored = new CellBoxes([Slab], null, null, [Hatch("hatch")]);

    BlockLaws.Law cell = Run(Machine("unlisted", DialCell()));
    BlockLaws.Law door = Run(
      Machine("doorless", doored, Table(("other", "hud", [])))
    );

    AssertFindings(
      Sides("unlisted", "names controls its table does not list: dial"),
      cell
    );
    Assert.Contains(
      "test:doorless-n names controls its table does not list: hatch",
      door.Findings
    );
  }

  // Fails when the law stops comparing a door's control kind with door.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void A_door_on_a_control_of_another_kind_is_named() {
    var cell = new CellBoxes([Slab], null, null, [Hatch("hatch")]);

    BlockLaws.Law law = Run(
      Machine("hudhatch", cell, Table(("hatch", "hud", [])))
    );

    AssertFindings(
      Sides("hudhatch", "has a door on a control whose kind is not door: hatch (hud)"),
      law
    );
  }

  // Fails when the law stops asking whether a table row has a control box or a door.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void A_table_row_with_no_box_is_named() {
    BlockLaws.Law law = Run(
      Machine("boxless", DialCell(), Table(("dial", "hud", []), ("ghost", "hud", [])))
    );

    AssertFindings(
      Sides("boxless", "lists controls with no box: ghost"),
      law
    );
  }

  // Fails when the law stops comparing alsoIds with the table's ids.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void An_also_id_that_is_a_control_is_named() {
    var cell = new CellBoxes(
      [Slab],
      [Slab, Dial, Gauge],
      [new ControlBox("dial", Dial), new ControlBox("gauge", Gauge)]
    );

    BlockLaws.Law law = Run(
      Machine(
        "aliased",
        cell,
        Table(("dial", "hud", []), ("gauge", "hud", ["dial"]))
      )
    );

    AssertFindings(
      Sides("aliased", "has alsoIds that are controls themselves: gauge (dial)"),
      law
    );
  }

  // Fails when the law stops judging a control box against the other control boxes and the body.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void Control_boxes_over_each_other_or_the_body_are_named() {
    Cuboidf under = new(0.25f, 0.25f, 0.25f, 0.75f, 0.75f, 0.75f);
    var overlap = new CellBoxes(
      [Slab],
      [Slab, Dial, Gauge],
      [new ControlBox("dial", Dial), new ControlBox("gauge", new Cuboidf(0.5f, 0.5f, 0.5f, 0.9f, 1f, 0.9f))]
    );
    var sunk = new CellBoxes(
      [Slab],
      [Slab, under],
      [new ControlBox("dial", under)]
    );
    var table = Table(("dial", "hud", []), ("gauge", "hud", []));

    BlockLaws.Law pair = Run(Machine("pair", overlap, table));
    BlockLaws.Law body = Run(Machine("sunk", sunk, Table(("dial", "hud", []))));

    AssertFindings(
      Sides("pair", "has a control box over another control or the body in cells: (1, 0, 0)"),
      pair
    );
    AssertFindings(
      Sides("sunk", "has a control box over another control or the body in cells: (1, 0, 0)"),
      body
    );
  }

  // Fails when the law stops comparing the block's boxes with the default beside a principalCell.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void Top_level_boxes_beside_a_principal_cell_are_named() {
    ExBlockDef def = Machine("twice", null, Table(("dial", "hud", [])), DialCell())
      .CollisionBox(0f, 0f, 0f, 1f, 0.5f, 1f);

    BlockLaws.Law law = Run(def);

    AssertFindings(
      Sides("twice", "declares collisionboxes or selectionboxes beside principalCell"),
      law
    );
  }

  // Fails when the law stops reading the placed cell's selection for the control's id.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void A_placed_cell_selecting_a_control_without_its_id_is_named() {
    ExBlockDef def = Machine(
      "idless",
      null,
      Table(("dial", "hud", [])),
      DialCell(),
      "test-stripsids"
    );

    BlockLaws.Law law = Run(def);

    AssertFindings(
      Sides("idless", "placed selects controls without their id: dial in principal"),
      law
    );
  }

  // Fails when the overlap check judges body boxes against each other, or a door leaf against a control box.
  [Fact]
  [PlantedDefect(typeof(ControlLaw), nameof(ControlLaw.Run))]
  public void Overlapping_body_boxes_and_a_shut_leaf_over_a_control_are_left_alone() {
    var cell = new CellBoxes(
      [Slab, HalfSlab],
      [Slab, HalfSlab, Dial],
      [new ControlBox("dial", Dial)],
      [Hatch("hatch")]
    );

    BlockLaws.Law law = Run(
      Machine("tidy", cell, Table(("dial", "hud", []), ("hatch", "door", [])))
    );

    Assert.Equal(1, law.Blocks);
    Assert.Equal(4, law.Cases);
    Assert.Empty(law.Findings);
  }

  private sealed class StripsIds : BlockFilledMegastructure {
    public override Cuboidf[] GetSelectionBoxes(
      IBlockAccessor blockAccessor,
      BlockPos pos
    ) => [.. base.GetSelectionBoxes(blockAccessor, pos).Select(b => new Cuboidf(b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2))];
  }
}
#endif
