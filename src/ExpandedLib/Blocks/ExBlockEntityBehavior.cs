using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks;

/// <summary>Block-entity behaviour base that persists whatever it declares. Mark a field
/// <see cref="PersistAttribute"/> and it needs no <see cref="DeclareState"/> entry at
/// all.</summary>
public abstract class ExBlockEntityBehavior(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) {
  private ExBlockState? _state;

  /// <summary>This behaviour's declared fields, built on first use.</summary>
  protected ExBlockState Persisted =>
    BlockEntityStateHost.GetOrCreate(this, ref _state, DeclareState);

  /// <summary>Declares the fields this behaviour persists. Called once, lazily.</summary>
  protected virtual void DeclareState(ExBlockState state) { }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    Persisted.ToTree(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    Persisted.FromTree(tree, worldAccessForResolve);
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
    IWorldAccessor worldForNewMappings,
    Dictionary<int, AssetLocation> oldBlockIdMapping,
    Dictionary<int, AssetLocation> oldItemIdMapping,
    int schematicSeed,
    bool resolveImports
  ) {
    base.OnLoadCollectibleMappings(
      worldForNewMappings,
      oldBlockIdMapping,
      oldItemIdMapping,
      schematicSeed,
      resolveImports
    );
    Persisted.LoadCollectibleMappings(
      worldForNewMappings,
      oldBlockIdMapping,
      oldItemIdMapping
    );
  }
}
