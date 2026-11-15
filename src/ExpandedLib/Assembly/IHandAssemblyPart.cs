using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>Implemented by a block, or a block behaviour, that refines where its hand-assembled part may stand.</summary>
public interface IHandAssemblyPart {
  /// <summary>Whether <paramref name="candidate"/> may be fixed at <paramref name="pos"/>.</summary>
  /// <param name="world">The world the part is placed in.</param>
  /// <param name="pos">The cell the candidate would fill.</param>
  /// <param name="candidate">The variant that would stand there.</param>
  /// <param name="failureCode">On a refusal, the placement failure code the player is shown; null on acceptance.</param>
  /// <returns>False when the candidate is refused.</returns>
  bool Allows(
    IWorldAccessor world,
    BlockPos pos,
    Block candidate,
    out string? failureCode
  );
}
