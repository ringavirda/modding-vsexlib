using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;

namespace ExpandedLib.Definitions;

/// <summary>
/// One variant group of an <see cref="ExBlockDef"/>, rendered into the block's code in declaration order.
/// </summary>
/// <param name="Name">The group's <c>code</c>, or for a codeless worldproperty group the last segment of
/// the property it loads from.</param>
/// <param name="States">The states the definition lists; empty for a worldproperty-sourced group.</param>
/// <param name="FromProperties">The <c>loadFromProperties</c> path, or null for an explicit state list.</param>
public sealed record ExVariantGroup(
  string Name,
  IReadOnlyList<string> States,
  string? FromProperties
) {
  /// <summary>
  /// Whether this group names a horizontal facing group (side or orientation), matched by its property
  /// path's last segment, not an axis group.
  /// </summary>
  public bool IsHorizontalFacing =>
    FromProperties?.Split(':')[^1] == "abstract/horizontalorientation"
    || (States.Count > 0 && States.All(ExOrientation.IsHorizontalSideWord));

  /// <summary>Whether this facing group's states are single letters (<c>n</c>), not full words (<c>north</c>).</summary>
  public bool UsesLetters => States.Count > 0 && States.All(s => s.Length == 1);
}
