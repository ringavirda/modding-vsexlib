using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Util;

namespace ExpandedLib.Industry.Casting;

/// <summary>One kind of casting station a mold pattern can name: the moulds it takes and the
/// sand meshes its cells show.</summary>
/// <param name="Code">The bare lowercase code patterns name in <c>mold.stations[].size</c>; one namespace across mods.</param>
/// <param name="Moulds">The moulds a pattern may declare for this station; never empty.</param>
/// <param name="SandBase">Shape asset of the flat rammed sand, shown once the station is rammed and not yet impressed.</param>
/// <param name="SandHalf">Shape asset of the half-height sand.</param>
/// <param name="SandItem">Item code of the moulding sand a player rams the station with; a wildcard such as <c>game:sand-*</c> matches a family.</param>
public sealed record CastingStationKind(
  string Code,
  IReadOnlyList<MouldKind> Moulds,
  string SandBase,
  string SandHalf,
  string SandItem
) {
  /// <summary>Whether a pattern may declare <paramref name="mould"/> at this station.</summary>
  public bool Takes(MouldKind mould) => Moulds.Contains(mould);

  /// <summary>Whether <paramref name="itemCode"/> is the moulding sand this station is rammed with.</summary>
  /// <param name="itemCode">Full item code, domain included; null or empty never matches.</param>
  public bool Matches(string? itemCode) =>
    !string.IsNullOrEmpty(itemCode) && WildcardUtil.Match(SandItem, itemCode);

  /// <summary>Whether <paramref name="other"/> declares the same station: the same code (ignoring case), the same
  /// sand meshes, the same sand item and the same set of moulds in any order.</summary>
  public bool SameAs(CastingStationKind other) =>
    string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase)
    && SandBase == other.SandBase
    && SandHalf == other.SandHalf
    && SandItem == other.SandItem
    && Moulds.Distinct().Order().SequenceEqual(other.Moulds.Distinct().Order());
}
