using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>Holds what hand assembly records on a placed part: whether it is fixed, the fasteners each joint spent, and the cap fitted to each face with the fasteners its joint spent. Absent records read as not fixed, with no fasteners and no caps.</summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorAssembled(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) {
  private const string FixedKey = "asmFixed";
  private const string JointKey = "asmFast";
  private const string CapKey = "asmCap";
  private const string CapFastenerKey = "asmCapFast";

  private readonly Dictionary<string, ItemStack> _fasteners = [];
  private readonly Dictionary<string, ItemStack> _caps = [];
  private readonly Dictionary<string, ItemStack> _capFasteners = [];

  /// <summary>Whether hand assembly fixed this part. A fixed part keeps its token.</summary>
  public bool Fixed { get; set; }

  /// <summary>The fasteners the joint on <paramref name="face"/> spent, held on the piece that made it; null when it spent none.</summary>
  /// <param name="face">The arm of this part the joint is on.</param>
  public ItemStack? FastenerAt(BlockFacing face) =>
    _fasteners.GetValueOrDefault(FaceKey(face));

  /// <summary>Records the fasteners the joint on <paramref name="face"/> spent.</summary>
  /// <param name="face">The arm of this part the joint is on.</param>
  /// <param name="stack">The spent fasteners, or null to clear the record.</param>
  public void SetFastener(BlockFacing face, ItemStack? stack) =>
    Set(FaceKey(face), stack);

  /// <summary>The fasteners the one joint of a part that is not a network node spent; null when it spent none.</summary>
  public ItemStack? Joint {
    get => _fasteners.GetValueOrDefault(JointKey);
    set => Set(JointKey, value);
  }

  /// <summary>The cap fitted to <paramref name="face"/>, held on the piece it closes.</summary>
  /// <param name="face">The face of this part the cap is on.</param>
  /// <returns>The cap stack, or null when the face has none.</returns>
  internal ItemStack? CapAt(BlockFacing face) =>
    _caps.GetValueOrDefault(face.Code);

  /// <summary>The fasteners the joint of the cap on <paramref name="face"/> spent.</summary>
  /// <param name="face">The face of this part the cap is on.</param>
  /// <returns>The fastener stack, or null when the cap spent none or the face has no cap.</returns>
  internal ItemStack? CapFastenersAt(BlockFacing face) =>
    _capFasteners.GetValueOrDefault(face.Code);

  /// <summary>Records the cap on <paramref name="face"/> and the fasteners its joint spent. Records only: the caller marks the block entity dirty and the networks to rebuild.</summary>
  /// <param name="face">The face of this part the cap is on.</param>
  /// <param name="cap">The cap stack, or null to clear both records of the face.</param>
  /// <param name="fasteners">The fasteners the cap's joint spent, or null for none; ignored when <paramref name="cap"/> is null.</param>
  internal void SetCap(BlockFacing face, ItemStack? cap, ItemStack? fasteners) {
    if (cap == null) {
      _caps.Remove(face.Code);
      _capFasteners.Remove(face.Code);
      return;
    }
    _caps[face.Code] = cap;
    if (fasteners == null)
      _capFasteners.Remove(face.Code);
    else
      _capFasteners[face.Code] = fasteners;
  }

  /// <summary>The record of the part at <paramref name="pos"/>.</summary>
  /// <returns>The record, or null when the block there has no block entity carrying one.</returns>
  internal static BEBehaviorAssembled? At(IBlockAccessor world, BlockPos pos) =>
    world.GetBlockEntity(pos)?.GetBehavior<BEBehaviorAssembled>();

  /// <summary>The stacks of every joint this part's record holds.</summary>
  internal IReadOnlyCollection<ItemStack> Spent => [.. _fasteners.Values];

  private static string FaceKey(BlockFacing face) => JointKey + face.Code;

  private void Set(string key, ItemStack? stack) {
    if (stack == null)
      _fasteners.Remove(key);
    else
      _fasteners[key] = stack;
  }

  /// <inheritdoc/>
  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    if (Fixed)
      tree.SetBool(FixedKey, true);
    foreach ((string key, ItemStack stack) in _fasteners)
      tree.SetItemstack(key, stack.Clone());
    foreach ((string code, ItemStack stack) in _caps)
      tree.SetItemstack(CapKey + code, stack.Clone());
    foreach ((string code, ItemStack stack) in _capFasteners)
      tree.SetItemstack(CapFastenerKey + code, stack.Clone());
  }

  /// <inheritdoc/>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    Fixed = tree.GetBool(FixedKey);
    _fasteners.Clear();
    foreach (string key in FaceKeys()) {
      ItemStack? stack = tree.GetItemstack(key);
      if (stack == null)
        continue;
      stack.ResolveBlockOrItem(worldAccessForResolve);
      _fasteners[key] = stack;
    }
    ReadFaces(tree, worldAccessForResolve, CapKey, _caps);
    ReadFaces(tree, worldAccessForResolve, CapFastenerKey, _capFasteners);
  }

  private static void ReadFaces(
    ITreeAttribute tree,
    IWorldAccessor world,
    string prefix,
    Dictionary<string, ItemStack> into
  ) {
    into.Clear();
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      ItemStack? stack = tree.GetItemstack(prefix + face.Code);
      if (stack == null)
        continue;
      if (!stack.ResolveBlockOrItem(world))
        continue;
      into[face.Code] = stack;
    }
  }

  /// <inheritdoc/>
  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  ) {
    base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
    foreach (ItemStack cap in _caps.Values)
      cap.Collectible?.OnStoreCollectibleMappings(
        Blockentity.Api.World,
        new DummySlot(cap),
        blockIdMapping,
        itemIdMapping
      );
  }

  /// <summary>Clears the fastener records of a part a schematic or a paste brings in, caps' included, and keeps <see cref="Fixed"/> and the caps, each re-resolved against the destination world and dropped when its collectible does not exist there: nothing was spent on its joints.</summary>
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
    _fasteners.Clear();
    _capFasteners.Clear();
    foreach (string code in _caps.Keys.ToList())
      if (
        !_caps[code]
          .FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForNewMappings)
      )
        _caps.Remove(code);
  }

  private static IEnumerable<string> FaceKeys() {
    yield return JointKey;
    foreach (BlockFacing face in BlockFacing.ALLFACES)
      yield return FaceKey(face);
  }
}
