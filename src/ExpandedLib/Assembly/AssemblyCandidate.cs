using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>One placement a held part could be fixed in.</summary>
/// <param name="Block">The variant that would stand at the target cell.</param>
/// <param name="Token">The orientation token the variant wears; empty for a part with one placement.</param>
/// <param name="Turn">The face letter a fitting's part points to, or null when the def has no <c>turn</c> group.</param>
/// <param name="Arms">The faces the variant couples on; empty for a block that is not a network node.</param>
/// <param name="OpenArms">The arms with no neighbour crossing they join: ends that stay open until capped.</param>
/// <param name="Joints">The joints fixing it makes now, as <see cref="HandAssemblyCandidates.JointsMade"/> counts them.</param>
public sealed record AssemblyCandidate(
  Block Block,
  string Token,
  string? Turn,
  IReadOnlyList<BlockFacing> Arms,
  IReadOnlyList<BlockFacing> OpenArms,
  int Joints
);
