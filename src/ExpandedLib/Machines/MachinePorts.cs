using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Machines;

/// <summary>
/// Network-port access shared by every fixed machine that reads or feeds a block network through a
/// connector face. A machine port is the network in the cell across the connector face.
/// </summary>
public static class MachinePorts {
  /// <summary>The block-network manager, resolved from the entity's API.</summary>
  public static BlockNetworkModSystem? NetworkSystem(this BlockEntity be) =>
    be.Api?.ModLoader.GetModSystem<BlockNetworkModSystem>();

  /// <summary>The network of type <typeparamref name="TNet"/> across <paramref name="connectorFace"/>
  /// from this machine, or <c>null</c> when the adjacent block exposes no connector facing back;
  /// shorthand for <see cref="ConnectedNetworkAt{TNet}"/> from the machine's own cell.</summary>
  public static TNet? ConnectedNetwork<TNet>(
    this BlockEntity be,
    BlockFacing connectorFace
  )
    where TNet : BlockNetwork =>
    be.ConnectedNetworkAt<TNet>(be.Pos, connectorFace);

  /// <summary>The network of type <typeparamref name="TNet"/> across <paramref name="connectorFace"/>
  /// from <paramref name="at"/>, which need not be this machine's own cell, or <c>null</c> when the
  /// adjacent block exposes no connector facing back.</summary>
  public static TNet? ConnectedNetworkAt<TNet>(
    this BlockEntity be,
    BlockPos at,
    BlockFacing connectorFace
  )
    where TNet : BlockNetwork =>
    be.NetworkSystem()
      ?.GetConnectedNetworkAcross(be.Api.World.BlockAccessor, at, connectorFace)
    as TNet;

  /// <summary>The network of type <typeparamref name="TNet"/> that owns <paramref name="pos"/>, or <c>null</c>.</summary>
  public static TNet? NetworkAt<TNet>(this BlockEntity be, BlockPos pos)
    where TNet : BlockNetwork => be.NetworkSystem()?.GetNetworkAt(pos) as TNet;

  /// <summary>The block entity of type <typeparamref name="T"/> at <paramref name="pos"/>, or <c>null</c> when none is there or the chunk is unloaded. While a fast-forward member's tick runs, the lookup records <paramref name="pos"/> as one of the member's couplings, so the member and the block entity share a group.</summary>
  public static T? BlockEntityAt<T>(this BlockEntity be, BlockPos pos)
    where T : BlockEntity {
    be.NetworkSystem()?.FastForward.Record(pos);
    return be.Api.World.BlockAccessor.GetBlockEntity(pos) as T;
  }
}
