using System.Collections.Generic;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>Holds what hand assembly records on a placed part: whether it is fixed and the fasteners each joint spent. Absent records read as not fixed, with no fasteners.</summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorAssembled(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) {
  private const string FixedKey = "asmFixed";
  private const string JointKey = "asmFast";

  private readonly Dictionary<string, ItemStack> _fasteners = [];

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
  }

  private static IEnumerable<string> FaceKeys() {
    yield return JointKey;
    foreach (BlockFacing face in BlockFacing.ALLFACES)
      yield return FaceKey(face);
  }
}
