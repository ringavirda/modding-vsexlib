using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>Implemented by a block that takes an item fitted onto it by hand while it stands placed, turning into another block: the player holds the item in the main hand and the block's <see cref="HandAssemblySpec.Tool"/> in the off hand and holds the sneak and the right button over the piece.</summary>
public interface IHandFitTarget {
  /// <summary>The block the fit would make, asked on the client for the ghost and the line and on the server before anything is spent.</summary>
  /// <param name="accessor">The accessor of the world the block stands in.</param>
  /// <param name="pos">The cell of the placed block.</param>
  /// <param name="held">The stack in the player's main hand; never null, count at least 1.</param>
  /// <param name="failureCode">When the result is null, the placement failure code the player is shown, without the <c>game:placefailure-</c> prefix; null otherwise.</param>
  /// <returns>The block that would stand at <paramref name="pos"/>, or null when the held item does not fit here.</returns>
  Block? FitResult(
    IBlockAccessor accessor,
    BlockPos pos,
    ItemStack held,
    out string? failureCode
  );

  /// <summary>Makes the swap at <paramref name="pos"/> and takes one of the held item out of <paramref name="held"/>, unless the player is in creative. Runs on the server after the range, claim, tool and fastener checks passed and before the fasteners are spent, so a refusal here costs nothing; the target also checks here whatever only the server can see, such as a jam.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="pos">The cell of the placed block.</param>
  /// <param name="player">The fitting player.</param>
  /// <param name="held">The slot of the main hand.</param>
  /// <param name="failureCode">When the result is false, the placement failure code the player is shown, without the <c>game:placefailure-</c> prefix.</param>
  /// <returns>True when the swap was made; false, with nothing changed, when it was refused.</returns>
  bool Fit(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer player,
    ItemSlot held,
    ref string failureCode
  );
}
