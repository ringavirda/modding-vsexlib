using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace ExpandedLib.Structures;

/// <summary>Which plane a text grid draws: rows and columns map to two world axes, depth to the third.</summary>
public enum GridPlane {
  Horizontal,
  SliceX,
  FaceZ,
}

/// <summary>How a text grid is read.</summary>
/// <param name="SpaceAdvancesColumn">Whether a space is a gap rather than a spacer between cells.</param>
/// <param name="Empty">The glyph that draws nothing but still advances the column.</param>
/// <param name="Anchor">The glyph whose position <see cref="CellGrid.AnchorCell"/> reports, or null.</param>
public sealed record GridOptions(
  bool SpaceAdvancesColumn = false,
  char Empty = '.',
  char? Anchor = null
);

/// <summary>
/// Turns rows of symbols into a cell list for one plane (<see cref="GridPlane"/>); <see cref="Add"/> is
/// called once per level and the accumulated cells are read from <see cref="Cells"/>.
/// </summary>
public sealed class CellGrid {
  private readonly GridPlane _plane;
  private readonly int _originA;
  private readonly int _originB;
  private readonly GridOptions _options;
  private readonly List<LayoutCell> _cells = new();
  private readonly HashSet<(int X, int Y, int Z)> _occupied = new();
  private readonly HashSet<char> _drawn = new();

  /// <summary>Starts an empty grid in <paramref name="plane"/> at the given column/row origin.</summary>
  public CellGrid(
    GridPlane plane,
    int originA,
    int originB,
    GridOptions? options = null
  ) {
    _plane = plane;
    _originA = originA;
    _originB = originB;
    _options = options ?? new GridOptions();
  }

  /// <summary>Every cell drawn so far, across every <see cref="Add"/> call, in drawing order.</summary>
  public IReadOnlyList<LayoutCell> Cells => _cells;

  /// <summary>True when <paramref name="symbol"/> was drawn by any <see cref="Add"/> call.</summary>
  public bool Drawn(char symbol) => _drawn.Contains(symbol);

  /// <summary>The position <see cref="GridOptions.Anchor"/> was drawn at, or null when never drawn.</summary>
  public (int X, int Y, int Z)? AnchorCell { get; private set; }

  /// <summary>Draws one level of the grid at <paramref name="depth"/>.</summary>
  /// <exception cref="System.InvalidOperationException">A cell this call draws already sits at a position an earlier call drew.</exception>
  public CellGrid Add(int depth, string grid) {
    string[] rows = grid.Replace("\r", "").Split('\n');
    TrimBlankEnds(rows, out int first, out int last);

    int rowIndex = 0;
    for (int r = first; r <= last; r++) {
      int col = 0;
      foreach (char ch in rows[r]) {
        bool blank = ch is ' ' or '\t';
        if (blank && !_options.SpaceAdvancesColumn)
          continue; // a spacer: neither drawn nor counted

        if (ch != _options.Empty) {
          (int x, int y, int z) = Resolve(depth, rowIndex, col);
          if (!_occupied.Add((x, y, z)))
            throw new System.InvalidOperationException(
              $"Layout grid draws two cells at ({x},{y},{z})."
            );
          _cells.Add(new LayoutCell(x, y, z, ch));
          _drawn.Add(ch);
          if (_options.Anchor == ch)
            AnchorCell ??= (x, y, z);
        }
        col++; // every glyph advances the column
      }
      rowIndex++;
    }
    return this;
  }

  // Maps (depth, rowIndex, col) to a world cell for this grid's plane.
  private (int X, int Y, int Z) Resolve(int depth, int rowIndex, int col) =>
    _plane switch {
      GridPlane.Horizontal => (_originA + col, depth, _originB + rowIndex),
      GridPlane.SliceX => (depth, _originB - rowIndex, _originA + col),
      _ => (_originA + col, _originB - rowIndex, depth), // FaceZ
    };

  // Indices of the first and last non-blank rows. first > last when every row is blank.
  private static void TrimBlankEnds(string[] rows, out int first, out int last) {
    first = 0;
    last = rows.Length - 1;
    while (first <= last && IsBlank(rows[first]))
      first++;
    while (last >= first && IsBlank(rows[last]))
      last--;
  }

  private static bool IsBlank(string row) {
    foreach (char ch in row)
      if (ch is not (' ' or '\t'))
        return false;
    return true;
  }
}
