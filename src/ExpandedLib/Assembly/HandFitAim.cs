using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>What a held item fits onto at a cell: the client's aim and the server's first check read it alike.</summary>
internal static class HandFitAim {
  /// <summary>Block.Replaceable at or above which a block is the air or the plant a placement replaces; a fit never aims at one.</summary>
  private const int ReplaceableFrom = 6000;

  /// <summary>The target at <paramref name="pos"/> and the block it would make.</summary>
  /// <param name="accessor">The accessor of the world.</param>
  /// <param name="pos">The aimed cell.</param>
  /// <param name="held">The main-hand stack.</param>
  /// <param name="failureCode">On a null result from an <see cref="IHandFitTarget"/>, the code it gave; null when the cell is no target.</param>
  /// <returns>The target and the swapped block; null when the cell holds a replaceable block, a block that is no <see cref="IHandFitTarget"/>, or a target whose <see cref="IHandFitTarget.FitResult"/> answers null.</returns>
  public static (IHandFitTarget Target, Block Result)? Of(
    IBlockAccessor accessor,
    BlockPos pos,
    ItemStack held,
    out string? failureCode
  ) {
    failureCode = null;
    Block block = accessor.GetBlock(pos);
    if (
      block.Replaceable >= ReplaceableFrom
      || block is not IHandFitTarget target
    )
      return null;
    Block? result = target.FitResult(accessor, pos, held, out failureCode);
    return result == null ? null : (target, result);
  }
}
