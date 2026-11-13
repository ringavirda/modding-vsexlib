using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>Which cell a <c>use</c> with an <c>id</c> placed, read from the cells whose block the use
/// changed.</summary>
internal static class SetupUsePlaced {
  /// <summary>The one cell the use placed a block in.</summary>
  /// <param name="appeared">Each cell that held another block (or air) before the use and holds a
  /// block now, with whether a block entity stands there.</param>
  /// <param name="id">The step's id, named in <paramref name="problem"/>.</param>
  /// <param name="problem">Why no cell is named: the use placed nothing, or placed several blocks of
  /// which none alone has a block entity; null when a cell is returned.</param>
  /// <returns>The only cell, else the only one with a block entity; null when neither is
  /// one.</returns>
  public static BlockPos? Of(
    IReadOnlyList<(BlockPos Pos, bool Entity)> appeared,
    string id,
    out string? problem
  ) {
    problem = null;
    if (appeared.Count == 1)
      return appeared[0].Pos;
    if (appeared.Count == 0) {
      problem = $"\"{id}\" placed nothing: the use changed no block.";
      return null;
    }
    BlockPos[] hosts = [.. appeared.Where(a => a.Entity).Select(a => a.Pos)];
    if (hosts.Length == 1)
      return hosts[0];
    problem =
      $"\"{id}\" is ambiguous: the use placed {appeared.Count} blocks, {hosts.Length} with a block entity.";
    return null;
  }
}
