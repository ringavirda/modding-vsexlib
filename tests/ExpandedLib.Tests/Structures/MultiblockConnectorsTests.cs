using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The connector check: a layout cell marked <c>Connector</c> is satisfied only by an occupant
/// whose network connector opens the way the drawing says.</summary>
public class MultiblockConnectorsTests {
  private static readonly BlockPos Anchor = new(0, 10, 0);

  #region The demand

  [Fact]
  public void A_node_facing_into_the_structure_leaves_it_incomplete() {
    Assert.False(Scene.WithNode("s").Completes());
  }

  [Fact]
  public void A_node_facing_the_way_the_drawing_says_completes_it() {
    Assert.True(Scene.WithNode("n").Completes());
  }

  [Fact]
  public void A_node_whose_faces_are_a_superset_completes_it_too() {
    // The demand is a subset test.
    Assert.True(Scene.WithNode("ns").Completes());
  }

  [Fact]
  public void A_plain_block_cannot_satisfy_a_connector_cell() {
    // The wanted code is a wildcard.
    Assert.False(Scene.WithPlainBlock().Completes());
  }

  [Fact]
  public void The_demanded_face_turns_with_the_structure() {
    // Authored north; at 90 degrees the cell wants west.
    Assert.False(Scene.WithNode("n", angle: 90).Completes());
    Assert.True(Scene.WithNode("w", angle: 90).Completes());
  }

  [Fact]
  public void A_misfaced_cell_is_reported_as_the_face_it_wants() {
    Scene scene = Scene.WithNode("s");

    scene.Completes();

    Assert.Contains("open to 'n'", scene.Rig.MissingReport);
  }

  #endregion

  #region The layout

  [Fact]
  public void A_layout_marking_no_connector_emits_no_attribute() {
    JToken? attributes = ExBlockDef
      .Create("exlib", "probe", "plain")
      .MultiblockLayout(l =>
        l.Legend('M', "exlib:probe-*")
          .Legend('F', "exlib:filler")
          .Layer(0, "MF")
      )
      .ToJson()["attributes"];

    Assert.Null(attributes?["multiblockConnectors"]);
  }

  [Fact]
  public void A_connector_on_a_glyph_the_drawing_never_uses_is_refused() {
    var thrown = Assert.Throws<System.InvalidOperationException>(() =>
      ExBlockDef
        .Create("exlib", "probe", "undrawn")
        .MultiblockLayout(l =>
          l.Legend('M', "exlib:probe-*")
            .Legend('Y', "exlib:probe-node-*")
            .Connector('Y', BlockFacing.NORTH)
            .Layer(0, "M")
        )
    );

    Assert.Contains("never draws", thrown.Message);
  }

  [Fact]
  public void A_connector_on_a_glyph_with_no_legend_entry_is_refused() {
    var thrown = Assert.Throws<System.InvalidOperationException>(() =>
      ExBlockDef
        .Create("exlib", "probe", "nolegend")
        .MultiblockLayout(l =>
          l.Legend('M', "exlib:probe-*")
            .Connector('Y', BlockFacing.NORTH)
            .Layer(0, "M")
        )
    );

    Assert.Contains("no Legend entry", thrown.Message);
  }

  [Fact]
  public void Two_faces_on_one_cell_are_both_demanded() {
    var connectors = (JObject)
      ExBlockDef
        .Create("exlib", "probe", "both")
        .MultiblockLayout(l =>
          l.Legend('M', "exlib:probe-*")
            .Legend('P', "exlib:probe-node-*")
            .Connector('P', BlockFacing.NORTH, BlockFacing.SOUTH)
            .Layer(0, "MP")
        )
        .ToJson()["attributes"]!["multiblockConnectors"]!;

    Assert.Equal(["n", "s"], connectors.Properties().Select(p => p.Name));
  }

  #endregion

  #region The scene

  /// <summary>A two-cell probe: the machine, and one cell south of it the drawing wants open to the
  /// north.</summary>
  private sealed class Scene {
    private readonly TestMegablock _machine;

    private Scene(StructureRig rig, TestMegablock machine) {
      Rig = rig;
      _machine = machine;
    }

    public StructureRig Rig { get; }

    public static Scene WithNode(string token, int angle = 0) =>
      Build(
        angle,
        TestNetworkBlock.Create("test", token, 900, $"exlib:probe-node-{token}")
      );

    public static Scene WithPlainBlock() =>
      Build(0, TestBlocks.Configure(new Block(), "exlib:probe-node-n", 901));

    private static Scene Build(int angle, Block occupant) {
      var world = new TestWorld();
      var machine = new TestMegablock { Angle = angle };
      world.Place(
        Anchor,
        TestBlocks.Configure(new Block(), "exlib:probe-n", 1),
        machine
      );
      world.Attach(machine);

      StructureRig rig = StructureRig.Around(world, machine, Def(), angle);
      rig.Occupy(rig.Cell(0, 0, 1), occupant);
      return new Scene(rig, machine);
    }

    /// <summary>Raises the footprint and lets the machine's monitor decide.</summary>
    public bool Completes() {
      Rig.Raise();
      Rig.World.Initialize(_machine);
      return Rig.AwaitCompletion();
    }

    private static ExBlockDef Def() =>
      ExBlockDef
        .Create("exlib", "probe")
        .MultiblockLayout(l =>
          l.Legend('M', "exlib:probe-*")
            .Legend('Y', "exlib:probe-node-*")
            .Connector('Y', BlockFacing.NORTH)
            .Layer(0, "M\nY")
        );
  }

  #endregion
}
