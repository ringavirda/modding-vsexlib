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

  // Fails when complete: all is skipped, or the structure check gets plot-local cells.
  [Fact]
  public void Complete_all_raises_the_structure_cells_the_drawing_leaves_to_it() {
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
    Assert.NotEqual(0, fixture.Test.GetBlock(new BlockPos(100, 52, 98)).Id);
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
}
