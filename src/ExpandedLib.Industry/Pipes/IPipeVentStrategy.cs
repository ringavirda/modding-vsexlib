using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Optional per-network strategy for gas vents: open connectors that draw gas away as a sink,
/// not a leak. Supplied through the network factory or a node block's
/// <see cref="IPipeVentSource"/>; a node with neither vents nothing.
/// </summary>
public interface IPipeVentStrategy {
  /// <summary>
  /// Returns <c>true</c> when the open <paramref name="face"/> of <paramref name="node"/> at
  /// <paramref name="pos"/> is a vent given its <paramref name="neighbour"/>; on <c>true</c>,
  /// <paramref name="ventPos"/> is where vent effects play.
  /// </summary>
  bool TryClassifyVent(
    IBlockAccessor blockAccessor,
    BlockNetworkNode node,
    BlockPos pos,
    BlockFacing face,
    Block neighbour,
    out BlockPos ventPos
  );

  /// <summary>
  /// Draws gas from the collected <paramref name="vents"/> into <paramref name="state"/>, playing
  /// vent feedback. A <paramref name="liquid"/> run vents nothing.
  /// </summary>
  /// <returns>Litres vented.</returns>
  float Vent(
    IReadOnlyList<BlockPos> vents,
    PipeNetworkState state,
    bool liquid,
    BlockNetworkModSystem manager
  );
}
