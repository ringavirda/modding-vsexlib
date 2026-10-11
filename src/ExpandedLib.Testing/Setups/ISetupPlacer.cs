#if GAME_GE_1_22
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// Places a block the composed placement kinds cannot (setups.md, Placers and stand-ins); registered
/// with <see cref="SetupPlacers.Register"/> before the first case.
/// </summary>
public interface ISetupPlacer
{
  /// <summary>Whether this placer places <paramref name="code"/>; the first registered placer that
  /// takes a code places it alone.</summary>
  /// <param name="code">A block code, such as <c>ppex:boilercornish-n</c>.</param>
  bool Takes(string code);

  /// <summary>What the game does instead of this placer for <paramref name="code"/>, or null when
  /// the placer departs from the game nowhere; printed once per case as
  /// <c>stand-in &lt;code&gt;: &lt;text&gt;</c>.</summary>
  /// <param name="code">A code <see cref="Takes"/> answered true for.</param>
  string? StandIn(string code);

  /// <summary>Places <paramref name="block"/> at <paramref name="pos"/> in <paramref name="scene"/>'s
  /// world.</summary>
  /// <param name="scene">The scene being built.</param>
  /// <param name="block">The loaded block <see cref="Takes"/> accepted.</param>
  /// <param name="pos">The cell, in world coordinates.</param>
  void Place(SetupScene scene, Block block, BlockPos pos);
}
#endif
