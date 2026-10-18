using System;
using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// The physical pipe network, type "pipeflow": a gas run whose nodes, edges and implicit step live in a
/// <see cref="PipeFlowStore"/>. It is not registered, so no world builds one yet.
/// </summary>
internal sealed class PipeFlowNetwork(BlockNetworkModSystem system)
  : BlockNetwork(system) {
  public override string NetworkType => "pipeflow";

  public override int StepIntervalMs => 200;

  /// <summary>The run's node store and edges.</summary>
  public PipeFlowStore Store { get; } = new();

  public override void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  ) {
    if (dt <= 0)
      return;
    foreach (var (key, index) in Store.Nodes)
      Store.SetFrozen(index, world.GetChunkAtBlockPos(key.Pos) == null);
    Store.Step(dt);
  }

  /// <summary>Writes the live store's entry for <paramref name="key"/> into <paramref name="host"/>, never the published display values.</summary>
  /// <exception cref="System.Collections.Generic.KeyNotFoundException">The store holds no such node.</exception>
  public void SaveNode(NodeKey key, ITreeAttribute host) {
    int node = Store.IndexOf(key);
    PipeFlowSave.Write(
      host,
      key.Slot,
      new PipeNodeSave(
        Store.GasKg(node),
        0,
        0,
        Store.TemperatureC(node),
        Store.WallTemperatureC(node),
        0,
        0
      )
    );
  }

  /// <summary>Adds the node saved in <paramref name="host"/> at the slot of <paramref name="key"/> to the store, unless the store already holds it: while the run lives its store is authoritative and a saved entry is stale.</summary>
  /// <param name="key">The node's identity.</param>
  /// <param name="host">The host block entity's tree.</param>
  /// <param name="volumeM3">Void volume in m3, above zero.</param>
  /// <param name="heightM">Height of the node in m.</param>
  /// <param name="gasConstant">Specific gas constant of the run's gas in J/(kg K), above zero; steam's by default.</param>
  /// <param name="wall">The node's wall, restored at its saved temperature; none leaves the node wall-less.</param>
  /// <returns>True when the node was added; false when the store holds it or the host has no readable entry for the slot.</returns>
  public bool RestoreNode(
    NodeKey key,
    ITreeAttribute host,
    double volumeM3,
    double heightM,
    double gasConstant = 461.5,
    PipeWall? wall = null
  ) {
    if (Store.Nodes.ContainsKey(key))
      return false;
    if (!PipeFlowSave.TryRead(host, key.Slot, out var save))
      return false;
    int node = Store.AddNode(
      key,
      volumeM3,
      heightM,
      save.GasKg,
      save.TemperatureC,
      gasConstant
    );
    if (wall is { } fitted)
      Store.SetWall(node, fitted, save.WallTemperatureC);
    return true;
  }

  public override void OnTickPublished(
    IBlockAccessor world,
    BlockNetworkModSystem manager
  ) => Store.Publish();

  /// <summary>What the run carries; <see cref="FlowMedium.Ambient"/> until a source gives it one.</summary>
  public FlowMedium Medium { get; set; } = FlowMedium.Ambient;

  public override bool SupportsPartialSplit => true;

  /// <summary>Whether the runs carry equal media or either is ambient; any other pair is refused and stays apart.</summary>
  public override bool CanMerge(BlockNetwork other, IBlockAccessor world) =>
    other is PipeFlowNetwork run
    && (
      Medium == run.Medium
      || Medium == FlowMedium.Ambient
      || run.Medium == FlowMedium.Ambient
    );

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    var run = (PipeFlowNetwork)other;
    Store.Absorb(run.Store);
    if (Medium == FlowMedium.Ambient)
      Medium = run.Medium;
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) => throw new NotSupportedException("A pipeflow run splits by node keys.");

  public override void OnSplitFragment(
    BlockNetwork original,
    IReadOnlySet<NodeKey> fragmentKeys
  ) {
    var run = (PipeFlowNetwork)original;
    Store.Take(run.Store, fragmentKeys);
    Medium = run.Medium;
  }
}
