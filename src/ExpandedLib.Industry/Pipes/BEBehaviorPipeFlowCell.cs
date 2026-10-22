using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// One cell of a physical pipe run as a block-entity behaviour. The run builds the cell's node from
/// <see cref="Bore"/> on its next tick after the cell joins it, and the cell saves that node under
/// <c>pf0</c>. A cell whose network type is not "pipeflow" is left alone.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorPipeFlowCell(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) {
  private BlockNetworkModSystem? _system;

  // The pf0 entry loaded from the save; spent by the run building the node, or dropped by Initialize
  // when the run already holds the node.
  private ITreeAttribute? _loaded;

  /// <summary>The bore class of this cell's pipe, which sets its node's volume, wall and ratings; null builds no node.</summary>
  /// <remarks>The default is the "main" class registered for the pipe block's <see cref="BlockPipe.Tier"/> in <see cref="FlowBores"/>, and null when the block is not a pipe or none is registered.</remarks>
  public virtual FlowBore? Bore =>
    Blockentity.Block is BlockPipe { Tier: { } tier }
    && FlowBores.TryGet(tier, "main", out var bore)
      ? bore
      : null;

  private bool IsFlowCell =>
    NetworkMembership.MemberOf(Blockentity, "pipeflow") != null;

  private NodeKey Key => NodeKey.Centre(Blockentity.Pos);

  private PipeFlowNetwork? Run => _system?.GetNetworkAt(Key) as PipeFlowNetwork;

  /// <inheritdoc/>
  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (api.Side != EnumAppSide.Server)
      return;
    _system = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    if (Run is { } run && run.Store.Nodes.ContainsKey(Key))
      _loaded = null;
  }

  /// <inheritdoc/>
  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    if (!IsFlowCell)
      return;
    if (Run is { } run && run.Store.Nodes.ContainsKey(Key))
      run.SaveNode(Key, tree);
    else if (_loaded?.GetTreeAttribute(PipeFlowSave.Key(0)) is { } entry)
      tree[PipeFlowSave.Key(0)] = entry.Clone();
  }

  /// <inheritdoc/>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _loaded = null;
    if (!PipeFlowSave.TryRead(tree, 0, out _))
      return;
    _loaded = new TreeAttribute();
    _loaded[PipeFlowSave.Key(0)] = tree.GetTreeAttribute(PipeFlowSave.Key(0))
      .Clone();
  }

  internal ITreeAttribute? TakeLoaded() {
    ITreeAttribute? loaded = _loaded;
    _loaded = null;
    return loaded;
  }
}
