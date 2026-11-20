using ExpandedLib.Structures;
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
    if (block.Replaceable >= ReplaceableFrom || block is not IHandFitTarget target)
      return null;
    Block? result = target.FitResult(accessor, pos, held, out failureCode);
    return result == null ? null : (target, result);
  }

  /// <summary>What a held item fits onto at a cell through an <see cref="IHandFitCellTarget"/>.</summary>
  /// <param name="Target">The principal's block.</param>
  /// <param name="Principal">The principal's cell: the aimed cell itself, or the one a filler cell belongs to.</param>
  /// <param name="PrincipalBlock">The block at <paramref name="Principal"/>; its attributes carry the <see cref="HandAssemblySpec"/>.</param>
  /// <param name="At">The aimed cell and the hit in it.</param>
  /// <param name="Plan">What the fit costs besides the held item.</param>
  internal sealed record Cell(
    IHandFitCellTarget Target,
    BlockPos Principal,
    Block PrincipalBlock,
    HandFitPoint At,
    HandFitPlan Plan
  );

  /// <summary>The principal of a megablock a cell belongs to.</summary>
  /// <param name="accessor">The accessor of the world.</param>
  /// <param name="pos">The aimed cell.</param>
  /// <returns>The principal's cell when <paramref name="pos"/> is a filler cell with a principal; <paramref name="pos"/> itself otherwise.</returns>
  public static BlockPos PrincipalOf(IBlockAccessor accessor, BlockPos pos) =>
    accessor.GetBlockEntity(pos) is BlockEntityStructureFiller { Principal: { } principal }
      ? principal
      : pos;

  /// <summary>The cell target at <paramref name="pos"/>, reached through a filler cell when the cell is one.</summary>
  /// <param name="accessor">The accessor of the world.</param>
  /// <param name="pos">The aimed cell.</param>
  /// <param name="hit">The hit inside the cell; the centre when null.</param>
  /// <param name="held">The main-hand stack.</param>
  /// <param name="failureCode">On a null result from an <see cref="IHandFitCellTarget"/>, the code it gave; null otherwise.</param>
  /// <returns>The aim; null when the principal is no <see cref="IHandFitCellTarget"/>, holds a replaceable block, or answers no plan.</returns>
  public static Cell? OfCell(
    IBlockAccessor accessor,
    BlockPos pos,
    Vec3d? hit,
    ItemStack held,
    out string? failureCode
  ) {
    failureCode = null;
    BlockPos principal = PrincipalOf(accessor, pos);
    Block block = accessor.GetBlock(principal);
    if (block.Replaceable >= ReplaceableFrom || block is not IHandFitCellTarget target)
      return null;
    var at = new HandFitPoint(pos.Copy(), hit ?? new Vec3d(0.5, 0.5, 0.5));
    HandFitPlan? plan = target.Plan(accessor, principal, at, held, out failureCode);
    return plan == null ? null : new Cell(target, principal.Copy(), block, at, plan);
  }
}
