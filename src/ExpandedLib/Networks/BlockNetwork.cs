using System;
using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// Abstract base for all live block-network instances; a concrete subclass owns its typed state and
/// implements the type-specific operations. <see cref="BlockNetworkModSystem"/> does only
/// graph-level work.
/// </summary>
public abstract class BlockNetwork(BlockNetworkModSystem system) {
  /// <summary>Stable identity for this network instance.</summary>
  public Guid Id { get; } = Guid.NewGuid();

  /// <summary>Identifies the network type, e.g. "gas" or "molten".</summary>
  public abstract string NetworkType { get; }

  /// <summary>Every world position that belongs to this network.</summary>
  public HashSet<BlockPos> Nodes { get; } = [];

  /// <summary>Root position for root-anchored networks; <c>null</c> for unanchored networks and fragments.</summary>
  public BlockPos? RootPos { get; set; }

  /// <summary>The current network state object (typed by the concrete subclass).</summary>
  public object? State { get; protected set; }

  /// <summary>The network manager that owns this instance.</summary>
  public BlockNetworkModSystem? NetworkSystem { get; set; } = system;

  #region State persistence

  /// <summary>Injects <paramref name="state"/> into this network, called during world load before
  /// the first tick; override to cast to the concrete state type.</summary>
  public virtual void RestoreState(object? state) {
    State = state;
  }

  #endregion

  #region Broadcasting

  /// <summary>Sends the current typed state to every <see cref="INetworkNode"/> block entity in this
  /// network.</summary>
  public void BroadcastUpdate(IBlockAccessor blockAccessor) {
    OnBeforeBroadcast(blockAccessor);
    object? payload = GetStatePayload();
    foreach (var pos in Nodes) {
      if (blockAccessor.GetBlockEntity(pos) is INetworkNode receiver)
        receiver.OnNetworkUpdate(payload);
    }
  }

  /// <summary>Called by <see cref="BroadcastUpdate"/> before the payload is collected and dispatched.
  /// Override to update derived state (e.g. recalculate capacity).</summary>
  protected virtual void OnBeforeBroadcast(IBlockAccessor blockAccessor) { }

  /// <summary>Returns the typed state object sent to nodes during a broadcast.</summary>
  protected virtual object? GetStatePayload() => State;

  #endregion

  #region Lifecycle callbacks

  /// <summary>Returns <c>false</c> to veto a graph-level merge of two adjacent networks of the same
  /// type.</summary>
  public virtual bool CanMerge(BlockNetwork other, IBlockAccessor world) =>
    true;

  /// <summary>
  /// Called when <paramref name="other"/> merges into this network. Implementations combine state
  /// (e.g. weighted-average temperature, total volume).
  /// </summary>
  public abstract void OnMerge(BlockNetwork other, IBlockAccessor world);

  /// <summary>
  /// Called on a fresh fragment after fracture, where this instance is the new fragment and
  /// <paramref name="original"/> the fractured network. Distributes a proportional share of state.
  /// </summary>
  public abstract void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  );

  /// <summary>Called once per server tick for each live network of this type.</summary>
  public abstract void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  );

  /// <summary>Transfers persistent state from <paramref name="source"/> into this instance,
  /// called by <see cref="BlockNetworkModSystem.RebuildFromRoot"/>; no-op by default, override to
  /// copy typed state fields.</summary>
  public virtual void InheritStateFrom(BlockNetwork source) { }

  /// <summary>Called after this network's <see cref="Nodes"/> set changed; override to drop caches
  /// derived from the node set.</summary>
  public virtual void OnTopologyChanged() { }

  #endregion
}
