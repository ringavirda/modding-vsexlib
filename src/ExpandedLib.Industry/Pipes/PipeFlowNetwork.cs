using System;
using ExpandedLib.Networks;
using Vintagestory.API.Common;

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
    if (dt > 0)
      Store.Step(dt);
  }

  public override void OnTickPublished(
    IBlockAccessor world,
    BlockNetworkModSystem manager
  ) => Store.Publish();

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) =>
    throw new NotSupportedException("A pipeflow run does not merge yet.");

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) => throw new NotSupportedException("A pipeflow run does not split yet.");
}
