using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary>The one way a scenario fills an off-hand slot.</summary>
internal static class LiveOffHand {
  /// <summary>Puts <paramref name="stack"/> in <paramref name="slot"/> unless the game's off-hand slot
  /// would refuse it.</summary>
  /// <exception cref="StepFailure">The stack is refused; the slot is untouched.</exception>
  internal static void Put(ItemSlot slot, ItemStack? stack) {
    if (SetupOffHand.Refusal(stack) is { } refusal)
      throw new StepFailure(refusal, Suspect.Scenario);
    slot.Itemstack = stack;
  }
}
