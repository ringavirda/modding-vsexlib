using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// Base block entity for any block that is a node in a <see cref="BlockNetwork"/>. Graph membership
/// is held by a <see cref="BEBehaviorNetworkMember"/> this class hosts; the block entity itself
/// persists orientation and network state and forwards network updates to the concrete block entity.
/// </summary>
public abstract class BlockEntityNetworkNode : BlockEntity, INetworkNode {
  private readonly HostMembership _membership;
  private ExBlockState? _state;

  /// <summary>This node's declared fields, built on first use.</summary>
  protected ExBlockState Persisted =>
    BlockEntityStateHost.GetOrCreate(this, ref _state, DeclareState);

  /// <summary>Declares the fields this node persists beyond the network membership state above;
  /// called once, lazily.</summary>
  protected virtual void DeclareState(ExBlockState state) { }

  protected BlockEntityNetworkNode() {
    // Must be added here: Behaviors fans out both FromTreeAttributes and Initialize.
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
  }

  /// <summary>The network manager this node is registered with, resolved when the block entity
  /// initialises.</summary>
  public BlockNetworkModSystem? NetworkSystem {
    get => _membership.NetworkSystem;
    protected set => _membership.NetworkSystem = value;
  }

  /// <summary>This node's graph membership; reads the network type and saved state off the block
  /// entity as it needs them.</summary>
  private sealed class HostMembership(BlockEntityNetworkNode owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => owner.NetworkType;
      protected set => owner.NetworkType = value;
    }

    protected override object? SavedNetworkState => owner._savedNetworkState;
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetString("networkType", NetworkType);
    tree.SetString("orientation", Orientation);
    tree.SetStrings("possibleOrientations", PossibleOrientations);
    SerializeNetworkState(tree, _savedNetworkState);
    Persisted.ToTree(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    NetworkType = tree.GetString("networkType", null);
    Orientation = tree.GetString("orientation");
    // A legacy save holds this key as JSON text; a null array read falls back to that encoding.
    PossibleOrientations =
      tree.GetStrings("possibleOrientations")
      ?? ExTree.SafeDeserialize<string[]>(
        tree.GetString("possibleOrientations"),
        []
      );
    _savedNetworkState = DeserializeNetworkState(tree);
    Persisted.FromTree(tree, worldForResolving);
  }

  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  ) {
    base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
    Persisted.StoreCollectibleMappings(
      Api.World,
      blockIdMapping,
      itemIdMapping
    );
  }

  public override void OnLoadCollectibleMappings(
    IWorldAccessor worldForResolve,
    Dictionary<int, AssetLocation> oldBlockIdMapping,
    Dictionary<int, AssetLocation> oldItemIdMapping,
    int schematicSeed,
    bool resolveImports
  ) {
    base.OnLoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping,
      schematicSeed,
      resolveImports
    );
    Persisted.LoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping
    );
  }

  #region Persistence hooks - override in concrete BEs

  /// <summary>Returns <c>true</c> when <paramref name="state"/> is worth caching and restoring;
  /// override to require non-empty content.</summary>
  protected virtual bool IsNetworkStateMeaningful(object? state) =>
    state != null;

  /// <summary>Deserializes the network state written by <see cref="SerializeNetworkState"/>; returns
  /// <c>null</c> when none was saved. Called from <see cref="FromTreeAttributes"/>.</summary>
  protected virtual object? DeserializeNetworkState(ITreeAttribute tree) =>
    null;

  /// <summary>Serializes <paramref name="state"/> into <paramref name="tree"/> for save/reload.
  /// Called from <see cref="ToTreeAttributes"/>.</summary>
  protected virtual void SerializeNetworkState(
    ITreeAttribute tree,
    object? state
  ) { }

  #endregion

  #region INetworkNode
  /// <inheritdoc/>
  public string[] PossibleOrientations { get; set; } = [];

  /// <inheritdoc/>
  public string? Orientation { get; set; }

  /// <summary>Network state cached for the restore-on-load path.</summary>
  protected object? _savedNetworkState;
  protected object? _networkState;

  /// <inheritdoc/>
  public virtual bool HasConnectorAt(BlockFacing face) =>
    (Block as BlockNetworkNode)?.HasConnectorAt(face) ?? false;

  /// <inheritdoc/>
  public virtual void OnNetworkUpdate(object? state) {
    _networkState = state;
    if (IsNetworkStateMeaningful(state))
      _savedNetworkState = state;
    else
      _savedNetworkState = null;
  }

  /// <summary>Whether this node currently severs the network at its position; override to break
  /// connectivity dynamically.</summary>
  public virtual bool IsConnectionBroken() => false;

  /// <inheritdoc/>
  public virtual void OnOpenConnectorsChanged(BlockFacing[] openFaces) { }

  /// <inheritdoc/>
  public virtual void OnLeak(
    BlockFacing[] leakingFaces,
    bool isLiquid,
    float intensity
  ) { }

  /// <inheritdoc/>
  public abstract string NetworkType { get; set; }
  #endregion
}
