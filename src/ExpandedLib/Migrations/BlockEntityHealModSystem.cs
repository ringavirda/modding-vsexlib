using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Migrations;

/// <summary>
/// Server-side self-healer for orphaned block entities: a block whose <see cref="BlockEntity"/> was
/// lost to a deserialization failure or a desync. A block whose <see cref="Block.EntityClass"/>
/// resolves to a <see cref="BlockEntityRegisterAttribute"/> type but has no live BE gets a fresh one.
/// </summary>
public class BlockEntityHealModSystem : ChunkColumnSweeperModSystem {
  // Block ids whose declared entityClass resolves to a [BlockEntityRegister] type. Built lazily, once
  // the world's block list exists.
  private readonly HashSet<int> _healableBlockIds = [];

  /// <summary>Builds the healable block-id set on first use; returns false if this world has none.</summary>
  protected override bool BuildWork() {
    BuildHealableSet();
    return _healableBlockIds.Count > 0;
  }

  private void BuildHealableSet() {
    // Block-entity types registered through the attribute system.
    HashSet<Type> ourBeTypes = CollectRegisteredBlockEntityTypes();
    if (ourBeTypes.Count == 0)
      return;

    // entityClass code -> registered here; resolved once per distinct code.
    Dictionary<string, bool> resolvedByCode = [];

    foreach (Block block in _sapi.World.Blocks) {
      if (block?.EntityClass == null || block.BlockId == 0)
        continue;

      if (!resolvedByCode.TryGetValue(block.EntityClass, out bool isOurs)) {
        isOurs = IsOurBlockEntity(block.EntityClass, ourBeTypes);
        resolvedByCode[block.EntityClass] = isOurs;
      }

      if (isOurs)
        _healableBlockIds.Add(block.BlockId);
    }

    if (_healableBlockIds.Count > 0)
      _sapi.Logger.Notification(
        Tag
          + " BE healer watching {0} block type(s) for orphaned block entities.",
        _healableBlockIds.Count
      );
  }

  /// <summary>True when <paramref name="entityClass"/> resolves to one of the registered BE types.</summary>
  private bool IsOurBlockEntity(string entityClass, HashSet<Type> ourBeTypes) {
    try {
      BlockEntity? be = _sapi.ClassRegistry.CreateBlockEntity(entityClass);
      return be != null && ourBeTypes.Contains(be.GetType());
    } catch {
      // Unknown or foreign class code: not registered here, leave it alone.
      return false;
    }
  }

  /// <summary>Scans every loaded assembly for concrete <see cref="BlockEntity"/> types carrying
  /// <see cref="BlockEntityRegisterAttribute"/>.</summary>
  private static HashSet<Type> CollectRegisteredBlockEntityTypes() {
    HashSet<Type> types = [];
    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
      foreach (Type t in ReflectionScan.GetCandidateTypes(asm))
        if (
          typeof(BlockEntity).IsAssignableFrom(t)
          && t.GetCustomAttribute<BlockEntityRegisterAttribute>() != null
        )
          types.Add(t);
    return types;
  }

  // Pre-reject: only the watched block ids can carry an orphanable BE.
  protected override bool ShouldVisit(int blockId) =>
    _healableBlockIds.Contains(blockId);

  protected override int VisitCell(
    IBlockAccessor ba,
    BlockPos pos,
    int blockId
  ) => HealOrphanAt(ba, pos) ? 1 : 0;

  protected override void OnColumnStreamedIn(
    int chunkX,
    int chunkZ,
    int healed
  ) =>
    _sapi.Logger.Notification(
      Tag + " Recreated {0} orphaned block entit(ies) in chunk column {1},{2}.",
      healed,
      chunkX,
      chunkZ
    );

  protected override void OnStartupSweepComplete(int total) =>
    _sapi.Logger.Notification(
      Tag
        + " Startup sweep recreated {0} orphaned block entit(ies) across loaded chunks.",
      total
    );

  /// <summary>Sweeps every currently loaded chunk and recreates any orphaned block entities,
  /// returning how many were healed.</summary>
  public int HealLoadedChunks() => SweepAllLoadedChunks();

  /// <summary>Spawns a fresh block entity when the block at <paramref name="pos"/> declares an
  /// <see cref="Block.EntityClass"/> but has no live one, returning <c>true</c>; a healthy block, an
  /// empty cell or a block without one is left untouched.</summary>
  public bool HealOrphanAt(IBlockAccessor ba, BlockPos pos) {
    string? entityClass = ba.GetBlock(pos)?.EntityClass;
    if (entityClass == null)
      return false;

    // A live BE means the block is healthy; only missing ones get recreated.
    if (ba.GetBlockEntity(pos) != null)
      return false;

    try {
      // Creates the BE and runs CreateBehaviors + Initialize for it.
      ba.SpawnBlockEntity(entityClass, pos);
      ba.GetBlockEntity(pos)?.MarkDirty(true);
      return true;
    } catch (Exception e) {
      _sapi.Logger.Warning(
        Tag + " Failed to recreate block entity '{0}' at {1}: {2}",
        entityClass,
        pos,
        e.Message
      );
      return false;
    }
  }
}
