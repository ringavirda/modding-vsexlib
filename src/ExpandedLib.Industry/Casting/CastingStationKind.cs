using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Industry.Casting;

/// <summary>One kind of casting station a mold pattern can name: the moulds it takes and the
/// sand meshes its cells show.</summary>
/// <param name="Code">The bare lowercase code patterns name in <c>mold.stations[].size</c>; one namespace across mods.</param>
/// <param name="Moulds">The moulds a pattern may declare for this station; never empty.</param>
/// <param name="SandBase">Shape asset of the flat rammed sand, shown once the station is rammed and not yet impressed.</param>
/// <param name="SandHalf">Shape asset of the half-height sand.</param>
public sealed record CastingStationKind(
  string Code,
  IReadOnlyList<MouldKind> Moulds,
  string SandBase,
  string SandHalf
) {
  /// <summary>Whether a pattern may declare <paramref name="mould"/> at this station.</summary>
  public bool Takes(MouldKind mould) => Moulds.Contains(mould);

  /// <summary>Whether <paramref name="other"/> declares the same station: the same code (ignoring case), the same
  /// sand meshes and the same set of moulds in any order.</summary>
  public bool SameAs(CastingStationKind other) =>
    string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase)
    && SandBase == other.SandBase
    && SandHalf == other.SandHalf
    && Moulds.Distinct().Order().SequenceEqual(other.Moulds.Distinct().Order());
}
