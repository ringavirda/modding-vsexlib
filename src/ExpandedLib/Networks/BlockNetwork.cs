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

  /// <summary>Identifies the network type, e.g. "pipe", "molten" or "mpenergy".</summary>
  public abstract string NetworkType { get; }

  /// <summary>Every world position that belongs to this network.</summary>
  public HashSet<BlockPos> Nodes { get; } = [];

  /// <summary>Every node of this network by key. Maintained by <see cref="BlockNetworkModSystem"/> beside <see cref="Nodes"/>; a type whose members keep to slot 0 holds one key per entry of <see cref="Nodes"/>.</summary>
  public HashSet<NodeKey> NodeKeys { get; } = [];

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

  /// <summary>Whether this type takes its share of a fracture by node keys through <see cref="OnSplitFragment(BlockNetwork, IReadOnlySet{NodeKey})"/> instead of by proportion. False by default.</summary>
  public virtual bool SupportsPartialSplit => false;

  /// <summary>
  /// Called instead of <see cref="OnSplitFragment(BlockNetwork, IBlockAccessor)"/> on a fresh fragment when
  /// <see cref="SupportsPartialSplit"/> is true. Moves exactly the entries of <paramref name="fragmentKeys"/> out
  /// of <paramref name="original"/>'s stores.
  /// </summary>
  /// <param name="original">The fractured network, already dissolved from the graph.</param>
  /// <param name="fragmentKeys">The keys now in this fragment.</param>
  /// <exception cref="NotSupportedException">The type does not override this while claiming <see cref="SupportsPartialSplit"/>.</exception>
  public virtual void OnSplitFragment(
    BlockNetwork original,
    IReadOnlySet<NodeKey> fragmentKeys
  ) =>
    throw new NotSupportedException(
      $"{GetType().Name} sets SupportsPartialSplit without overriding OnSplitFragment(original, fragmentKeys)."
    );

  /// <summary>Milliseconds of server time between <see cref="OnTick"/> calls for this type. The system's listener
  /// fires every 200 ms and steps the network once this much time has accrued, passing the accrued time as
  /// <c>dt</c> capped at twice the interval. 1000 by default; read each tick, so a value below 200 steps once per
  /// listener fire.</summary>
  public virtual int StepIntervalMs => 1000;

  /// <summary>Called once per listener fire for each network that stepped in that fire, after every network has
  /// stepped.</summary>
  /// <param name="world">The block accessor the tick ran with.</param>
  /// <param name="manager">The owning graph.</param>
  public virtual void OnTickPublished(
    IBlockAccessor world,
    BlockNetworkModSystem manager
  ) { }

  /// <summary>Called each time <see cref="StepIntervalMs"/> of server time has passed for each live network of this type.</summary>
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
