using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// Addressing molten cells on a block entity that hosts more than one.
/// <c>GetBehavior&lt;BEBehaviorMoltenCell&gt;()</c> returns only the first match; these helpers select
/// by the cell's declared <see cref="BEBehaviorMoltenCell.Key"/>.
/// </summary>
public static class MoltenCellHost {
  /// <summary>The molten cell on <paramref name="be"/> whose declared key is <paramref name="key"/>, or
  /// <c>null</c> when none matches.</summary>
  public static BEBehaviorMoltenCell? MoltenCell(
    this BlockEntity? be,
    string key
  ) =>
    be
      ?.Behaviors.OfType<BEBehaviorMoltenCell>()
      .FirstOrDefault(c => c.Key == key);

  /// <summary>Every molten cell on <paramref name="be"/>, in declaration order.</summary>
  public static IEnumerable<BEBehaviorMoltenCell> MoltenCells(
    this BlockEntity? be
  ) => be?.Behaviors.OfType<BEBehaviorMoltenCell>() ?? [];
}
