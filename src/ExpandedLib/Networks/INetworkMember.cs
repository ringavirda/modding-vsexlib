using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// One cell's participation in a block network, as the graph walk sees it. Answered either by a
/// <c>BEBehaviorNetworkMember</c> on the block entity or by the block itself through <see cref="INetworkConnector"/>.
/// </summary>
public interface INetworkMember {
  /// <summary>The network this cell belongs to, e.g. "pipe", "molten".</summary>
  string NetworkType { get; }

  /// <summary>Position-aware network type, for a member whose type varies by cell.</summary>
  string NetworkTypeAt(IBlockAccessor world, BlockPos pos) => NetworkType;

  /// <summary>True when this cell exposes a network connector on <paramref name="face"/>.</summary>
  bool HasConnectorAt(IBlockAccessor world, BlockPos pos, BlockFacing face);

  /// <summary>When true, this cell is a fixed endpoint and is excluded from neighbour discovery.</summary>
  bool IsNetworkEndPoint => false;

  /// <summary>Whether this cell currently severs the network at its position, e.g. a closed valve.</summary>
  bool IsConnectionBroken(IBlockAccessor world, BlockPos pos) => false;

  /// <summary>Whether this cell will physically join <paramref name="neighbour"/>, beyond the geometric checks; implementations must answer symmetrically.</summary>
  bool AcceptsNeighbour(Block neighbour) => true;
}
