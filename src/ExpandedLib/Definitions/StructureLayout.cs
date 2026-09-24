using System.Collections.Generic;
using ExpandedLib.Structures;

namespace ExpandedLib.Definitions;

/// <summary>One parsed cell of a structure layout: its offset from the principal and the legend symbol.</summary>
public readonly record struct LayoutCell(int X, int Y, int Z, char Symbol);

/// <summary>
/// Parses a stack of horizontal ASCII layers into cells over <see cref="CellGrid"/>, rows along +Z,
/// columns along +X. The multiblock and filler builders parse through <c>ThreePlaneDraw</c>
/// and <see cref="CellGrid"/> themselves and do not call it.
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
