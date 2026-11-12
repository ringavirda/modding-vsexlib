using Vintagestory.API.Common;

namespace ExpandedLib.Storage;

/// <summary>
/// Implemented by an item class whose stacks differ in drawn size, so <see cref="ExFitsOneBlock"/> can
/// measure the held stack.
/// </summary>
public interface IStoredSize {
  /// <summary>The length of the stack's drawn shape lying flat, in voxels (16 is one block).</summary>
  double PosedLength(ItemStack stack);
}
