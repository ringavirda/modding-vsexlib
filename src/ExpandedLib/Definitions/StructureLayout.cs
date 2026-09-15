using System.Collections.Generic;
using ExpandedLib.Structures;

namespace ExpandedLib.Definitions;

/// <summary>One parsed cell of a structure layout: its offset from the principal and the legend symbol.</summary>
public readonly record struct LayoutCell(int X, int Y, int Z, char Symbol);

/// <summary>
/// Parses the ASCII layer diagrams the multiblock and filler DSLs are authored with, over the shared
/// <see cref="CellGrid"/> core. A structure is a stack of 2D grids, rows along +Z, columns along +X.
/// </summary>
public static class StructureLayout {
  /// <summary>Parses the layers into their non-empty cells, each carrying its legend symbol.</summary>
  public static List<LayoutCell> Parse(
    int xLeft,
    int zTop,
    IReadOnlyList<(int Y, string Grid)> layers
  ) {
    var grid = new CellGrid(GridPlane.Horizontal, xLeft, zTop);
    foreach ((int y, string cells) in layers)
      grid.Add(y, cells);
    return new List<LayoutCell>(grid.Cells);
  }
}
