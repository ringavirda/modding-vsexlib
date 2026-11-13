#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>Which of a part's candidates a placement takes. Each set field narrows the list the
/// server offers; <paramref name="Steps"/> then counts the right-clicks that step on from the first
/// left, wrapping after the last.</summary>
/// <param name="Block">Code of the variant to take, or null for any.</param>
/// <param name="Arms">The faces the variant couples on as letters in <see cref="BlockFacing.ALLFACES"/> order (n e s w u d), or null for any.</param>
/// <param name="Turn">The face letter a fitting's part points to, or null for any.</param>
/// <param name="Steps">Right-clicks before the fix, 0 or more.</param>
internal sealed record AssemblyPick(
  string? Block = null,
  string? Arms = null,
  string? Turn = null,
  int Steps = 0
)
{
  /// <summary>The refusal code of a click that offers no candidate a pick names.</summary>
  public const string NoCandidate = "harness-nocandidate";

  /// <summary>The candidate this pick names among <paramref name="candidates"/>.</summary>
  /// <returns>The candidate, or null when the pick's filters leave none.</returns>
  public AssemblyCandidate? Choose(IReadOnlyList<AssemblyCandidate> candidates)
  {
    List<AssemblyCandidate> left =
    [
      .. candidates.Where(c =>
        (Block == null || c.Block.Code.ToString() == Block)
        && (Arms == null || SetupRunFamily.Letters(c.Arms) == Arms)
        && (Turn == null || c.Turn == Turn)
      ),
    ];
    return left.Count == 0 ? null : left[Steps % left.Count];
  }
}
#endif
