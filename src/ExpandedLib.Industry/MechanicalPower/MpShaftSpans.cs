using System;
using System.Collections.Generic;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A stretch of a straight run that breaks the span or the overhang rule.</summary>
/// <param name="FromCell">Index of the support the stretch starts at, or 0 for a stretch before the first support or a run with none.</param>
/// <param name="Cells">Length of the stretch in cells: between two supports, or past the nearest support to a free end.</param>
/// <param name="Limit">The span or overhang in cells the stretch exceeds.</param>
/// <param name="Overhang">True for a free end, false for a gap between two supports.</param>
internal readonly record struct MpSpanBreach(
  int FromCell,
  int Cells,
  int Limit,
  bool Overhang
);

/// <summary>The support rules of a straight shaft run.</summary>
internal static class MpShaftSpans {
  /// <summary>Finds the first stretch of a straight run that breaks the rules: no two consecutive supports farther apart than <paramref name="span"/>, and no free end more than <paramref name="overhang"/> cells past its nearest support. A run with no support overhangs by its whole length.</summary>
  /// <param name="supported">One entry per cell along the run, true where the cell is a support.</param>
  /// <param name="span">Most cells between two consecutive supports, one or more.</param>
  /// <param name="overhang">Most cells a free end may stand past its last support, zero or more.</param>
  /// <returns>The first breach from the start of the run, or null when the run holds.</returns>
  public static MpSpanBreach? Breach(
    IReadOnlyList<bool> supported,
    int span,
    int overhang
  ) {
    int last = -1;
    for (int i = 0; i < supported.Count; i++) {
      if (!supported[i])
        continue;
      if (last < 0 && i > overhang)
        return new MpSpanBreach(0, i, overhang, true);
      if (last >= 0 && i - last > span)
        return new MpSpanBreach(last, i - last, span, false);
      last = i;
    }
    int tail = last < 0 ? supported.Count : supported.Count - 1 - last;
    return tail > overhang
      ? new MpSpanBreach(Math.Max(last, 0), tail, overhang, true)
      : null;
  }
}
