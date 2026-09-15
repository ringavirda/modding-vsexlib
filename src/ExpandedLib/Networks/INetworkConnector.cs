using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// A port: a face another network may couple to, on a block that is not itself a graph member. A
/// port is a valid connection target and is never added to the graph.
/// </summary>
public interface INetworkConnector : INetworkMember {
  /// <summary>True when this block exposes a network connector on <paramref name="face"/>.</summary>
  bool HasConnectorAt(BlockFacing face);

  /// <summary>Position-aware connector test, defaulting to <see cref="HasConnectorAt(BlockFacing)"/>.</summary>
  bool INetworkMember.HasConnectorAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) => HasConnectorAt(face);

  /// <summary>Reads the block entity at <paramref name="pos"/>; reports false when there is none.</summary>
  bool INetworkMember.IsConnectionBroken(IBlockAccessor world, BlockPos pos) =>
    world.GetBlockEntity(pos) is INetworkNode node && node.IsConnectionBroken();
}
