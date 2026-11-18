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

  /// <summary>Where <paramref name="candidate"/> stands in the list of placements offered at the aim; lower ranks are offered first.</summary>
  /// <remarks>The candidates are stable-sorted by the sum of this rank over the block and its behaviours that implement the interface, so equal ranks keep the order the look gives them. The default ranks every candidate 0 and changes nothing.</remarks>
  /// <param name="world">The world the part is placed in.</param>
  /// <param name="selection">The block the player aims at.</param>
  /// <param name="candidate">The variant that would stand there.</param>
  /// <returns>The rank; negative values come before the default.</returns>
  int CandidateRank(
    IWorldAccessor world,
    BlockSelection selection,
    Block candidate
  ) => 0;
}
