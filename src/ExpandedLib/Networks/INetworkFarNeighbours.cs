using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// Implemented by a block entity or one of its behaviours that joins its cell to cells it shares no face with, as a belt
/// does. The graph walk joins two cells through it only when both list each other and each resolves as a member of the
/// same network type, as <see cref="INetworkMember.AcceptsNeighbour"/> asks both cells of a pair.
/// </summary>
public interface INetworkFarNeighbours {
  /// <summary>The cells this holder lists, none of them its own; empty when it lists none. Read from held state alone, so the answer is the same while the far chunk is unloaded.</summary>
  IEnumerable<BlockPos> FarNeighbours { get; }
}
