using System.Collections.Generic;

namespace ExpandedLib.Catalogues;

/// <summary>One occupant of a bay row: the cell its run starts at and how many cells it
/// spans.</summary>
/// <param name="Start">First cell of the run, from 0 at the principal end.</param>
/// <param name="Length">Cells the run spans. At least one.</param>
public readonly record struct BayRun(int Start, int Length) {
  /// <summary>One past the last cell of the run.</summary>
  public int End => Start + Length;

  /// <summary>Whether <paramref name="cell"/> lies in this run.</summary>
  public bool Covers(int cell) => cell >= Start && cell < End;

  /// <summary>Whether this run and <paramref name="other"/> share a cell.</summary>
  public bool Overlaps(BayRun other) => Start < other.End && other.Start < End;
}

/// <summary>
/// A row of storage cells filled by occupants of declared length. Length is the only axis; nothing
/// stacks upward and nothing sits side by side across the row.
/// </summary>
public static class BayLayout {
  /// <summary>Whether a run of <paramref name="length"/> starting at <paramref name="start"/> fits
  /// in a row of <paramref name="cells"/> without touching <paramref name="taken"/>.</summary>
  public static bool Fits(
    IReadOnlyList<BayRun> taken,
    int cells,
    int start,
    int length
  ) {
    if (length < 1 || start < 0 || start + length > cells)
      return false;

    var run = new BayRun(start, length);
    foreach (BayRun held in taken)
      if (held.Overlaps(run))
        return false;
    return true;
  }

  /// <summary>The lowest cell a run of <paramref name="length"/> can start at, or null when the
  /// row has no gap wide enough.</summary>
  public static int? Fit(IReadOnlyList<BayRun> taken, int cells, int length) {
    for (int start = 0; start + length <= cells; start++)
      if (Fits(taken, cells, start, length))
        return start;
    return null;
  }

  /// <summary>Index into <paramref name="taken"/> of the run covering <paramref name="cell"/>, or
  /// null when that cell is empty.</summary>
  public static int? IndexAt(IReadOnlyList<BayRun> taken, int cell) {
    for (int i = 0; i < taken.Count; i++)
      if (taken[i].Covers(cell))
        return i;
    return null;
  }

  /// <summary>Cells of a <paramref name="cells"/>-long row that no run covers.</summary>
  public static int FreeCells(IReadOnlyList<BayRun> taken, int cells) {
    int used = 0;
    foreach (BayRun run in taken)
      used += run.Length;
    return cells - used;
  }
}
