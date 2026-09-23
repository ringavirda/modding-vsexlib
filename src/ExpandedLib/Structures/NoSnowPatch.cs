using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>Keeps weather snow off <see cref="NoSnowCells"/>. A block class that overrides either
/// method without calling the base escapes it.</summary>
[HarmonyPatch]
internal static class NoSnowPatch {
  [HarmonyPatch(typeof(Block), nameof(Block.AllowSnowCoverage))]
  [HarmonyPostfix]
  public static void RefuseCoverage(BlockPos blockPos, ref bool __result) {
    if (NoSnowCells.IsMarked(blockPos))
      __result = false;
  }

  [HarmonyPatch(typeof(Block), nameof(Block.GetSnowCoveredVariant))]
  [HarmonyPostfix]
  public static void KeepVariant(
    Block __instance,
    BlockPos pos,
    ref Block __result
  ) {
    if (NoSnowCells.IsMarked(pos))
      __result = __instance;
  }
}
