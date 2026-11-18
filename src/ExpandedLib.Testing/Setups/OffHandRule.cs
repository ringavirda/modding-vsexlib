#if GAME_GE_1_22
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// The off-hand check of <see cref="TestPlayer.HoldOffHand"/>: a stack the game's off-hand slot would
/// not take is refused.
/// </summary>
public static class OffHandRule
{
  /// <summary>Whether the game's off-hand slot takes <paramref name="stack"/>: its storage flags include
  /// Offhand (256).</summary>
  /// <param name="stack">The stack to test.</param>
  /// <returns>True when the slot takes it.</returns>
  public static bool Takes(ItemStack stack) => SetupOffHand.Takes(stack);
}
#endif
