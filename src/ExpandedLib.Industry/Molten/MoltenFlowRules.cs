namespace ExpandedLib.Industry.Molten;

/// <summary>How metal crosses a connection between two molten cells, returned by
/// <see cref="IMoltenCell.FlowRules"/>. A connection applies rules only when both cells return
/// them, taking the smaller rate, the larger gap, conveying if both convey, horizontal-only if
/// either is.</summary>
/// <param name="FlowRate">Most metal units one connection moves per tick; 0 or less moves
/// nothing.</param>
/// <param name="MinFlowGap">Smallest difference in metal units between the two cells that moves
/// any metal; a drain fitting (<see cref="IMoltenCell.AcceptsSubMinimumFlow"/>) takes a smaller
/// one. 1 or less sets no floor.</param>
/// <param name="Conveys">When true, a receiver farther than the giver from the nearest flow source
/// takes the whole difference; otherwise, and toward the source or between equal distances,
/// half.</param>
/// <param name="HorizontalOnly">When true, no metal crosses the cell's up and down faces.</param>
public readonly record struct MoltenFlowRules(
  int FlowRate,
  int MinFlowGap,
  bool Conveys,
  bool HorizontalOnly
);
