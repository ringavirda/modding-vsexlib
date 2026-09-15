using System;
using System.Linq;

namespace ExpandedLib.Catalogues;

/// <summary>
/// One state of a piece part-way through a sequence process, addressed by (thickness, accepting
/// family) rather than by thickness alone.
/// </summary>
/// <param name="Thickness">The gauge this state sits at, in block-space units.</param>
/// <param name="Element">Shape element drawing this state, or null when the whole shape file is
/// the stage.</param>
/// <param name="AcceptedBy">Machine families that take this state.</param>
/// <param name="Code">The item this state becomes when claimed, or null for a render-only
/// intermediate.</param>
/// <param name="Generate">Whether an item is built for <paramref name="Code"/>. False means the
/// code exists already.</param>
/// <param name="FormerCodes">Codes this stage's product used to have.</param>
/// <param name="HalfStep">A state a piece lands on part way through a rung: drawn, never
/// selectable, never a stopping point.</param>
public sealed record ProcessStage(
  float Thickness,
  string? Element,
  string[] AcceptedBy,
  string? Code,
  bool Generate = true,
  string[]? FormerCodes = null,
  bool HalfStep = false
) {
  /// <summary>Codes this product used to have. Empty rather than null.</summary>
  public string[] FormerCodes { get; init; } = FormerCodes ?? [];

  /// <summary>Whether the piece can be claimed here.</summary>
  public bool IsStoppingPoint => !string.IsNullOrWhiteSpace(Code);

  /// <summary>Whether this stage is a gauge the machine can be set to, everything except a
  /// half-step.</summary>
  public bool IsRung => !HalfStep;

  /// <summary>Whether <paramref name="family"/> takes this state.</summary>
  public bool IsAcceptedBy(string? family) =>
    family != null
    && AcceptedBy.Contains(family, StringComparer.OrdinalIgnoreCase);
}
