using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The layout walk of the live runner: its placement order, what it places, and the
/// structure checks it runs, over a <see cref="LiveFixture"/>.</summary>
public class LayoutWalkTests {
  private const string Blower = "twintubblower:blower-twintubblower-n";

  private static SetupLayout Drawing(string layout) =>
    LiveFixture.Case("[]", $"{{ layout: {layout} }}").Model.Drawn!;

  private static void Walk(
    LiveFixture fixture,
    SetupLayout layout,
    RunStep.PlayerPlace? place = null
  ) {
    foreach (bool _ in SetupLayoutWalk.Run(fixture.Context, layout, place)) { }
  }

  private static RunStep.PlayerPlace Stub(List<BlockPos> placed) =>
    (Block block, BlockPos at, BlockFacing face, out string? failure) => {
      placed.Add(at.Copy());
      failure = null;
      return true;
    };

  private static RunStep.PlayerPlace Clicks(
    List<(BlockPos At, BlockFacing Face)> clicks
  ) =>
    (Block block, BlockPos at, BlockFacing face, out string? failure) => {
      clicks.Add((at.Copy(), face));
      failure = null;
      return true;
    };

  private static LiveFixture Packed(string gathered, out SetupLayout layout) {
    ScenarioCase scenario = LiveFixture.Case(
      "[]",
      $$"""
      { pack: { scope: "all", gathered: { "game:stone": {{gathered}} } },
        layout: { legend: {
            "P": { "run": "main", "code": "game:stone" },
            "A": { "code": "game:stone" } },
          layers: { "0": ["PP", "A."] } } }
      """
    );
    layout = scenario.Model.Drawn!;
    var fixture = new LiveFixture();
    fixture.Context.Pack = SetupPack.Start(scenario.Model, _ => null);
    return fixture;
  }

  // Fails when the walk goes cell by cell over the grid, or a run is placed at its last entry.
  [Fact]
  public void The_plan_orders_entries_by_the_legend_and_cells_in_reading_order() {
    SetupLayout layout = Drawing(
      """
      { legend: {
          "P": { "run": "main", "code": "game:stone" },
          "A": { "code": "game:stone" },
          "Q": { "run": "main", "code": "game:stone" } },
        layers: { "0": ["PPQ", "AAA"] } }
      """
    );
    IReadOnlyList<LayoutPlacing> plan = SetupLayoutWalk.Plan(layout);
    Assert.Equal(2, plan.Count);
    Assert.Equal("main", plan[0].Run?.Id);
    Assert.Equal(
      [(0, 0, 0), (1, 0, 0), (2, 0, 0)],
      plan[0].Cells.Select(c => (c.X, c.Y, c.Z))
    );
    Assert.Equal('A', plan[1].Entry.Glyph);
    Assert.Equal(
      [(0, 0, 1), (1, 0, 1), (2, 0, 1)],
      plan[1].Cells.Select(c => (c.X, c.Y, c.Z))
    );
  }

  // Fails when a cells, port or open entry is placed as a block entry.
  [Fact]
  public void Cells_and_open_entries_place_nothing() {
    SetupLayout layout = Drawing(
      """
      { legend: {
          "A": { "id": "a", "code": "game:stone" },
          "c": { "cells": "a" },
          "o": { "open": true },
          "P": { "run": "main", "code": "game:stone" } },
        layers: { "1": ["PPo"], "0": ["Ac"] } }
      """
    );
    IReadOnlyList<LayoutPlacing> plan = SetupLayoutWalk.Plan(layout);
    Assert.Equal(['A', 'P'], plan.Select(p => p.Entry.Glyph));
    Assert.Equal([(0, 0, 0)], plan[0].Cells.Select(c => (c.X, c.Y, c.Z)));
  }

  // Fails when a run's id holds only its first cell.
  [Fact]
  public void A_run_is_laid_by_the_player_and_its_id_holds_every_cell() {
    var fixture = new LiveFixture();
    var placed = new List<BlockPos>();
    SetupLayout layout = Drawing(
      """
      { legend: {
          "P": { "run": "main", "code": "game:stone" },
          "A": { "code": "game:stone" } },
        layers: { "1": ["AAA"], "0": ["PPP"] } }
      """
    );
    Walk(fixture, layout, Stub(placed));
    Assert.Equal(3, placed.Count);
    Assert.Equal(3, fixture.Context.Setup.Placed("main")?.Cells.Count);
    Assert.Contains(
      "[scenario]   L1 layout P main ok: 3 cell(s) of main",
      fixture.Lines
    );
  }

  // Fails when the structure check is skipped.
  [Fact]
  public void A_cells_glyph_outside_the_structure_fails_naming_the_cell() {
    var fixture = LiveFixture.WithSamples();
    SetupLayout layout = Drawing(
      $$"""
      { origin: [0, -2],
        legend: {
          "B": { "id": "blower", "code": "{{Blower}}" },
          "b": { "cells": "blower" },
          "c": { "cells": "blower" } },
        layers: { "2": ["b", "b", "b"], "1": ["b", "b", "Bc"] } }
      """
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      Walk(fixture, layout)
    );
    Assert.Contains(
      "glyph 'c' at (1, 1, 0) is outside the cells of \"blower\"",
      failure.Message
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
  }

  // Fails when the structure check gets plot-local cells, or its cells are not mapped back to local.
  [Fact]
  public void The_blowers_drawn_structure_passes_its_check_over_the_plot() {
    var fixture = LiveFixture.WithSamples();
    SetupLayout layout = Drawing(
      $$"""
      { origin: [0, -2],
        legend: {
          "B": { "id": "blower", "code": "{{Blower}}" },
          "b": { "cells": "blower" } },
        layers: { "2": ["b", "b", "b"], "1": ["b", "b", "B"] } }
      """
    );
    Walk(fixture, layout);
    Assert.Equal(
      new BlockPos(0, 1, 0),
      fixture.Context.Setup.Placed("blower")?.Cell
    );
  }

  // Fails when the structure check is skipped, gets plot-local cells, or its cells are not mapped back to local.
  [Fact]
  public void A_drawn_structure_missing_a_cell_fails_naming_it_in_plot_cells() {
    var fixture = LiveFixture.WithSamples();
    SetupLayout layout = Drawing(
      $$"""
      { origin: [0, -2],
        legend: {
          "B": { "id": "blower", "code": "{{Blower}}" },
          "b": { "cells": "blower" } },
        layers: { "2": ["b", "b", "."], "1": ["b", "b", "B"] } }
      """
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      Walk(fixture, layout)
    );
    Assert.Contains(
      "cell (0, 2, 0) of the structure is drawn with no glyph",
      failure.Message
    );
  }

  // Fails when complete: all is not read from the entry, so its construction is never walked.
  [Fact]
  public void Complete_all_on_a_drawn_structure_goes_on_to_its_construction() {
    var fixture = LiveFixture.WithSamples();
    SetupLayout layout = Drawing(
      $$"""
      { origin: [0, -2],
        legend: {
          "B": { "id": "blower", "code": "{{Blower}}", "complete": "all" },
          "b": { "cells": "blower" } },
        layers: { "2": ["b", "b", "b"], "1": ["b", "b", "B"] } }
      """
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      Walk(fixture, layout)
    );
    Assert.Contains("needs the scenario player", failure.Message);
  }

  // Fails when a run is laid without the scenario player.
  [Fact]
  public void A_run_without_the_scenario_player_fails_the_entry() {
    var fixture = new LiveFixture();
    SetupLayout layout = Drawing(
      """
      { legend: { "P": { "run": "main", "code": "game:stone" } },
        layers: { "0": ["PPP"] } }
      """
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      Walk(fixture, layout)
    );
    Assert.Contains("\"run\" needs the scenario player.", failure.Message);
    Assert.Equal(Suspect.Scenario, failure.Suspect);
  }

  // Fails when the run's open faces are not checked.
  [Fact]
  public void A_run_open_face_that_is_no_connector_fails_the_run() {
    var fixture = new LiveFixture();
    var placed = new List<BlockPos>();
    SetupLayout layout = Drawing(
      """
      { legend: {
          "P": { "run": "main", "code": "game:stone" },
          "o": { "open": true } },
        layers: { "0": ["PPPo"] } }
      """
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      Walk(fixture, layout, Stub(placed))
    );
    Assert.Contains("is not a connector of", failure.Message);
  }

  // Fails when a run that turns is refused as a bend, or a cell is clicked from its list predecessor.
  [Fact]
  public void An_L_shaped_run_is_laid_cell_by_cell_from_the_cell_it_joins() {
    var fixture = new LiveFixture();
    var clicks = new List<(BlockPos At, BlockFacing Face)>();
    SetupLayout layout = Drawing(
      """
      { legend: { "P": { "run": "main", "code": "game:stone" } },
        layers: { "0": ["PP", ".P"] } }
      """
    );
    Walk(fixture, layout, Clicks(clicks));
    Assert.Equal(
      [
        (new BlockPos(100, 50, 100), BlockFacing.WEST),
        (new BlockPos(101, 50, 100), BlockFacing.EAST),
        (new BlockPos(101, 50, 101), BlockFacing.SOUTH),
      ],
      clicks
    );
    Assert.Equal(3, fixture.Context.Setup.Placed("main")?.Cells.Count);
  }

  // Fails when a branch's cell is clicked from the cell before it in the list, which is not adjacent.
  [Fact]
  public void A_tee_run_clicks_the_branch_from_the_junction() {
    var fixture = new LiveFixture();
    var clicks = new List<(BlockPos At, BlockFacing Face)>();
    SetupLayout layout = Drawing(
      """
      { legend: { "P": { "run": "main", "code": "game:stone" } },
        layers: { "0": ["PPP", ".P."] } }
      """
    );
    Walk(fixture, layout, Clicks(clicks));
    Assert.Equal(
      [
        (new BlockPos(100, 50, 100), BlockFacing.WEST),
        (new BlockPos(101, 50, 100), BlockFacing.EAST),
        (new BlockPos(102, 50, 100), BlockFacing.EAST),
        (new BlockPos(101, 50, 101), BlockFacing.SOUTH),
      ],
      clicks
    );
  }

  // Fails when a layout cell is placed without drawing from the pack, or the draw is not logged under its L label.
  [Fact]
  public void Layout_block_and_run_cells_draw_from_the_pack_under_their_labels() {
    LiveFixture fixture = Packed("3", out SetupLayout layout);
    Walk(fixture, layout, Stub([]));
    Assert.Equal(0, fixture.Context.Pack!.Held("game:stone"));
    Assert.Equal(
      [("L1", "game:stone", 1), ("L1", "game:stone", 1), ("L2", "game:stone", 1)],
      fixture.Context.Pack.Record([]).Draws
    );
  }

  // Fails when a short pack lets a layout cell down, or its failure is not printed under the entry's L label.
  [Fact]
  public void A_pack_short_of_a_layout_block_fails_that_entrys_line() {
    LiveFixture fixture = Packed("2", out SetupLayout layout);
    Assert.Throws<StepFailure>(() => Walk(fixture, layout, Stub([])));
    Assert.Contains(
      "[scenario]   L2 layout A game:stone FAIL: the pack holds 0 of game:stone; the step takes 1.",
      fixture.Lines
    );
  }
}
