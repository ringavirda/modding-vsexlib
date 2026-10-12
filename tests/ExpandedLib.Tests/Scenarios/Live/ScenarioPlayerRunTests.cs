using System.Linq;
using ExpandedLib.Testing.Live;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The scenario player's <c>run</c> step: its path, its look and its open faces (scenarios.md, <c>run</c>).</summary>
public class ScenarioPlayerRunTests {
  private static string Cells(
    System.Collections.Generic.IEnumerable<BlockPos> cells
  ) => string.Join(" ", cells.Select(c => $"{c.X},{c.Y},{c.Z}"));

  /// <summary>Fails when the path drops its first cell (S6).</summary>
  [Fact]
  public void The_path_lists_each_cell_once_from_the_first_to_the_last() {
    var cells = RunStep.Path(
      new BlockPos(0, 0, 0),
      [new BlockPos(2, 0, 0)],
      new BlockPos(2, 0, 2)
    );
    Assert.Equal("0,0,0 1,0,0 2,0,0 2,0,1 2,0,2", Cells(cells));
  }

  /// <summary>Fails when the first cell is clicked from the side its neighbour is not on (S14), a later cell from any face but the one looking toward it (S15), or a single cell from any face but up (S16).</summary>
  [Fact]
  public void Each_cell_is_clicked_on_the_face_that_looks_along_the_run() {
    var cells = RunStep.Path(
      new BlockPos(0, 0, 0),
      [new BlockPos(1, 0, 0)],
      new BlockPos(1, 0, 1)
    );
    Assert.Equal(
      "west east south",
      string.Join(" ", RunStep.ClickFaces(cells).Select(f => f.Code))
    );
    Assert.Equal(
      BlockFacing.UP,
      Assert.Single(RunStep.ClickFaces([new BlockPos(4, 0, 4)]))
    );
  }

  /// <summary>Fails when a turning path is called straight (S21), or a straight one has a turn (S22).</summary>
  [Fact]
  public void A_path_with_a_turn_names_the_turn_cell() {
    var bent = RunStep.Path(
      new BlockPos(0, 0, 0),
      [new BlockPos(2, 0, 0)],
      new BlockPos(2, 0, 2)
    );
    BlockPos turn = Assert.IsType<BlockPos>(RunStep.FirstTurn(bent));
    Assert.Equal((2, 0, 0), (turn.X, turn.Y, turn.Z));
    var straight = RunStep.Path(
      new BlockPos(0, 0, 0),
      [new BlockPos(2, 0, 0)],
      new BlockPos(4, 0, 0)
    );
    Assert.Null(RunStep.FirstTurn(straight));
  }

  /// <summary>Fails when the cell past the first end is taken from the wrong side (S23), or past the last (S24).</summary>
  [Fact]
  public void The_cells_beyond_a_run_continue_its_end_legs() {
    var cells = RunStep.Path(new BlockPos(1, 0, 0), [], new BlockPos(3, 0, 0));
    (BlockPos before, BlockPos after) = RunStep.Beyond(cells);
    Assert.Equal(("0,0,0", "4,0,0"), (Cells([before]), Cells([after])));
  }

  /// <summary>Fails when the hit point is fixed at the top (S25) or leaves the clicked face (S26).</summary>
  [Fact]
  public void The_hit_is_the_middle_of_the_clicked_face() {
    Vec3d east = ScenarioPlayer.Hit(BlockFacing.EAST);
    Vec3d down = ScenarioPlayer.Hit(BlockFacing.DOWN);
    Assert.Equal((1.0, 0.5, 0.5), (east.X, east.Y, east.Z));
    Assert.Equal((0.5, 0.0, 0.5), (down.X, down.Y, down.Z));
  }

  /// <summary>Fails when a leg along two axes is accepted (S7).</summary>
  [Fact]
  public void A_leg_along_two_axes_fails_the_step() {
    var e = Assert.Throws<StepFailure>(() =>
      RunStep.Path(new BlockPos(0, 0, 0), [], new BlockPos(1, 0, 1))
    );
    Assert.Equal(
      "the leg from [0, 0, 0] to [1, 0, 1] runs along more than one axis.",
      e.Message
    );
  }

  /// <summary>Fails when a blocked face passes (S8), or the failure does not name the face (S9).</summary>
  [Fact]
  public void An_open_face_that_is_not_a_connector_fails_naming_the_face() {
    string? failure = RunStep.OpenFailure(
      new BlockPos(3, 0, 2),
      BlockFacing.SOUTH,
      "platedpipes:pipe-plated-straight-we",
      f => f == BlockFacing.WEST
    );
    Assert.Equal(
      "face south of [3, 0, 2] is not a connector of platedpipes:pipe-plated-straight-we.",
      failure
    );
  }

  /// <summary>Fails when a face the block connects on is refused (S8).</summary>
  [Fact]
  public void An_open_face_that_is_a_connector_passes() {
    Assert.Null(
      RunStep.OpenFailure(
        new BlockPos(0, 0, 0),
        BlockFacing.WEST,
        "x:y",
        f => f == BlockFacing.WEST
      )
    );
  }

  /// <summary>Fails when the player stands anywhere but two blocks south of the cell (S10).</summary>
  [Fact]
  public void The_player_stands_two_blocks_south_of_the_cell() {
    Vec3d at = ScenarioPlayer.Stand(new BlockPos(10, 64, 20));
    Assert.Equal((10.5, 64.0, 22.5), (at.X, at.Y, at.Z));
  }

  /// <summary>Fails when yaw takes the wrong sign (S11), or pitch the wrong sign (S12).</summary>
  [Fact]
  public void The_player_looks_from_its_eye_toward_the_hit_point() {
    (float yaw, float pitch) = ScenarioPlayer.Look(
      new Vec3d(0, 1, 2),
      new Vec3d(0, 0, 0)
    );
    Assert.Equal(0f, yaw, 4);
    Assert.Equal(-System.Math.Atan2(1, 2), pitch, 4);
    (float east, _) = ScenarioPlayer.Look(
      new Vec3d(0, 0, 0),
      new Vec3d(1, 0, 0)
    );
    Assert.Equal(-System.Math.PI / 2, east, 4);
  }

  /// <summary>Fails when a run without a scenario player proceeds instead of failing the step (S13).</summary>
  [Fact]
  public void A_run_without_the_scenario_player_fails_the_step() {
    var fixture = new LiveFixture();
    fixture.Test.Register(
      new Vintagestory.API.Common.Block {
        BlockId = 9,
        Code = new Vintagestory.API.Common.AssetLocation("game:pipe"),
      }
    );
    var steps = LiveFixture
      .Case("""[{ run: { code: "game:pipe", from: [0,0,0], to: [1,0,0] } }]""")
      .Model.Steps;
    var e = Assert.Throws<StepFailure>(() => fixture.Run(steps));
    Assert.Equal("\"run\" needs the scenario player.", e.Message);
  }
}
