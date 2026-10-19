#if GAME_GE_1_22
using ExpandedLib.Catalogues;
using HarmonyLib;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>Applies <see cref="CraftSequenceGate"/> inside <see cref="GridRecipe.Matches"/>. The game
/// answers <c>MatchesGridRecipe</c> with the last handler subscribed, and the survival mod's
/// subscribes after every mod's <c>Start</c> and answers true, so the event alone does not turn a
/// sequenced recipe away.</summary>
[HarmonyPatch]
internal static class CraftSequenceGatePatch
{
  [HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches))]
  [HarmonyPrefix]
  public static bool RefuseSequenced(GridRecipe __instance, ref bool __result)
  {
    if (CraftSequenceGate.Allows(CraftSequenceRegistry.Shared, __instance))
      return true;
    __result = false;
    return false;
  }
}
#endif
