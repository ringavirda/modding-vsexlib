using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ThreePlaneDraw"/> is the shared three-plane draw both <c>MultiblockLayoutBuilder</c> and
/// <c>FillerLayoutBuilder</c> call. Layers, slices and faces are drawn by three separate branches, so
/// the transform hook and the anchor return are each checked from every branch on its own - a layout
/// that draws only a slice or only a face still needs both to work.
/// </summary>
public class ThreePlaneDrawTests {
  private static readonly GridOptions Options = new(Anchor: '0');

  [Fact]
  public void Draw_applies_the_transform_to_a_layers_only_grid() =>
    AssertTransformApplied(layers: [(0, "X")], slices: [], faces: []);

  [Fact]
  public void Draw_applies_the_transform_to_a_slices_only_grid() =>
    AssertTransformApplied(layers: [], slices: [(0, "X")], faces: []);

  [Fact]
  public void Draw_applies_the_transform_to_a_faces_only_grid() =>
    AssertTransformApplied(layers: [], slices: [], faces: [(0, "X")]);

  private static void AssertTransformApplied(
    IReadOnlyList<(int Y, string Grid)> layers,
    IReadOnlyList<(int X, string Grid)> slices,
    IReadOnlyList<(int Z, string Grid)> faces
  ) {
    var cells = new List<LayoutCell>();
    ThreePlaneDraw.Draw(
      layers,
      slices,
      faces,
      0,
      0,
      Options,
      cells,
      transform: static g => g.Replace('X', '0')
    );

    // The grid only ever sees 'X'; a cell drawn as '0' proves transform ran before CellGrid.Add.
    Assert.Single(cells);
    Assert.Equal('0', cells[0].Symbol);
  }

  [Fact]
  public void Draw_reports_the_anchor_when_only_a_slice_draws_it() {
    var cells = new List<LayoutCell>();
    var anchor = ThreePlaneDraw.Draw(
      layers: [],
      slices: [(0, "0")],
      faces: [],
      0,
      0,
      Options,
      cells
    );

    Assert.Equal((0, 0, 0), anchor);
  }

  [Fact]
  public void Draw_reports_the_anchor_when_only_a_face_draws_it() {
    var cells = new List<LayoutCell>();
    var anchor = ThreePlaneDraw.Draw(
      layers: [],
      slices: [],
      faces: [(0, "0")],
      0,
      0,
      Options,
      cells
    );

    Assert.Equal((0, 0, 0), anchor);
  }
}
