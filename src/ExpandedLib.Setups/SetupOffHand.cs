#if GAME_GE_1_22
using Vintagestory.API.Common;

namespace ExpandedLib.Scenarios;

/// <summary>The stacks the game's off-hand slot takes.</summary>
internal static class SetupOffHand
{
  /// <summary>Whether the game's off-hand slot takes <paramref name="stack"/>: its storage flags
  /// include <see cref="EnumItemStorageFlags.Offhand"/>.</summary>
  public static bool Takes(ItemStack stack) =>
    (stack.Collectible.GetStorageFlags(stack) & EnumItemStorageFlags.Offhand)
    != 0;

  /// <summary>Why <paramref name="stack"/> may not go in the off-hand slot.</summary>
  /// <param name="stack">What would be put there; null is an empty slot.</param>
  /// <returns>Null for a null stack or a stack the slot takes; else the refusal.</returns>
  public static string? Refusal(ItemStack? stack)
  {
    if (stack == null || Takes(stack))
      return null;
    string code = stack.Collectible.Code.ToString();
    int flags = (int)stack.Collectible.GetStorageFlags(stack);
    return $"{code} cannot go in the off-hand slot: the game's off-hand slot takes only a stack whose storage flags include Offhand (256), and {code} has {flags}.";
  }
}
#endif
