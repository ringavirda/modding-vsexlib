using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// A point on a cell face where a node can join another: the face centre (<paramref name="Edge"/> null) or the
/// spot beside one of the face's edges.
/// </summary>
/// <param name="Face">The face of the cell the crossing lies on.</param>
/// <param name="Edge">The edge of that face the spot lies beside, or null for the face centre.</param>
public readonly record struct NodeCrossing(
  BlockFacing Face,
  BlockFacing? Edge = null
);
