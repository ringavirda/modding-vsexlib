using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>One stack a loaded recipe registry makes.</summary>
/// <param name="Registry">The registry: <c>grid</c>, <c>cooking</c>, <c>barrel</c>, <c>alloy</c>,
/// <c>smithing</c>, <c>knapping</c> or <c>clayforming</c>.</param>
/// <param name="Type">Block or item.</param>
/// <param name="Code">The stack's code, domain-qualified.</param>
public sealed record LoadedOutput(
  string Registry,
  EnumItemClass Type,
  AssetLocation Code
);
