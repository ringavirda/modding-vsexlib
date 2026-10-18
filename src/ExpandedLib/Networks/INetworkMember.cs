using System.Collections.Generic;
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

  /// <summary>Whether this cell will physically join <paramref name="neighbour"/>, beyond the geometric checks. The walk asks both cells of a pair, so a refusal from either keeps them apart whichever side the walk starts from.</summary>
  bool AcceptsNeighbour(Block neighbour) => true;

  /// <summary>The slots that hold a node at <paramref name="pos"/>. Slot 0 alone by default, one node per cell.</summary>
  IReadOnlyList<byte> Slots(IBlockAccessor world, BlockPos pos) => NodeSlots.CentreOnly;

  /// <summary>The crossings of the node <paramref name="key"/>. By default the centre slot crosses at the centre of every face with a connector, and any other slot has none.</summary>
  IEnumerable<NodeCrossing> Crossings(IBlockAccessor world, NodeKey key) {
    if (key.Slot != 0)
      yield break;
    foreach (BlockFacing face in BlockFacing.ALLFACES)
      if (HasConnectorAt(world, key.Pos, face))
        yield return new NodeCrossing(face);
  }
}

/// <summary>Shared slot lists for <see cref="INetworkMember.Slots"/>.</summary>
internal static class NodeSlots {
  internal static readonly byte[] CentreOnly = [0];
}
