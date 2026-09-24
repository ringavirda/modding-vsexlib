namespace ExpandedLib.Industry.Molten;

/// <summary>How metal crosses a connection between two molten cells, returned by
/// <see cref="IMoltenCell.FlowRules"/>. A connection applies rules only when both cells return
/// them, taking the smaller rate, the larger gap, conveying if both convey, horizontal-only if
/// either is.</summary>
/// <param name="FlowRate">Most metal units one connection moves per tick, when both its cells
/// return rules; 0 or less moves nothing.</param>
/// <param name="MinFlowGap">Smallest difference in metal units between the two cells that moves
/// any metal, when both return rules; a drain fitting
/// (<see cref="IMoltenCell.AcceptsSubMinimumFlow"/>) is exempt. 1 or less sets no floor.</param>
/// <param name="Conveys">When true, and both cells return rules, a receiver farther than the giver
/// from the nearest flow source takes the whole difference. A drain fitting and a downhill edge
/// take the whole difference whatever this is; any other pair moves half.</param>
/// <param name="HorizontalOnly">When true, and both cells return rules, no metal crosses the
/// connection through this cell's up and down faces.</param>
public readonly record struct MoltenFlowRules(
  int FlowRate,
  int MinFlowGap,
  bool Conveys,
  bool HorizontalOnly
);
