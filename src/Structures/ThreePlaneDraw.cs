using System;
using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace ExpandedLib.Structures;

/// <summary>
/// Draws whichever of a layout's three plane kinds (<see cref="GridPlane.Horizontal"/> layers,
/// <see cref="GridPlane.SliceX"/> slices, <see cref="GridPlane.FaceZ"/> faces - a layout may declare
/// any mix) are non-empty, one <see cref="CellGrid"/> per kind. Shared by
/// <see cref="ExpandedLib.Definitions.MultiblockLayoutBuilder"/> and <see cref="FillerLayoutBuilder"/>.
/// </summary>
internal static class ThreePlaneDraw {
  /// <param name="layers">Depth-and-grid pairs for <see cref="GridPlane.Horizontal"/>, in any Y order.</param>
  /// <param name="slices">Depth-and-grid pairs for <see cref="GridPlane.SliceX"/>, in any X order.</param>
  /// <param name="faces">Depth-and-grid pairs for <see cref="GridPlane.FaceZ"/>, in any Z order.</param>
  /// <param name="originA">World value of each grid's first column; meaning depends on plane (see <see cref="CellGrid"/>).</param>
  /// <param name="originB">World value of each grid's first row; meaning depends on plane.</param>
  /// <param name="options">Reading rules and anchor glyph shared by all three grids.</param>
  /// <param name="cells">Receives every drawn cell, across all three planes, in draw order.</param>
  /// <param name="transform">Applied to each grid line before <see cref="CellGrid.Add"/> sees it (e.g.
  /// folding a second anchor spelling to the canonical glyph); identity when omitted.</param>
  /// <returns>The position <see cref="CellGrid.AnchorCell"/> reported in whichever plane drew the
  /// anchor glyph first, or null when no plane drew it.</returns>
  public static (int X, int Y, int Z)? Draw(
    IReadOnlyList<(int Y, string Grid)> layers,
    IReadOnlyList<(int X, string Grid)> slices,
    IReadOnlyList<(int Z, string Grid)> faces,
    int originA,
    int originB,
    GridOptions options,
    List<LayoutCell> cells,
    Func<string, string>? transform = null
  ) {
    transform ??= static grid => grid;
    (int X, int Y, int Z)? anchor = null;

    if (layers.Count > 0) {
      var grid = new CellGrid(GridPlane.Horizontal, originA, originB, options);
      foreach ((int y, string g) in layers)
        grid.Add(y, transform(g));
      cells.AddRange(grid.Cells);
      anchor ??= grid.AnchorCell;
    }
    if (slices.Count > 0) {
      var grid = new CellGrid(GridPlane.SliceX, originA, originB, options);
      foreach ((int x, string g) in slices)
        grid.Add(x, transform(g));
      cells.AddRange(grid.Cells);
      anchor ??= grid.AnchorCell;
    }
    if (faces.Count > 0) {
      var grid = new CellGrid(GridPlane.FaceZ, originA, originB, options);
      foreach ((int z, string g) in faces)
        grid.Add(z, transform(g));
      cells.AddRange(grid.Cells);
      anchor ??= grid.AnchorCell;
    }
    return anchor;
  }
}
