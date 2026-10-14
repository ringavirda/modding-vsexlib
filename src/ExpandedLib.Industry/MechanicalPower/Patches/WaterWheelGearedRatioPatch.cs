#if GAME_GE_1_22
using System.Reflection;
using HarmonyLib;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Industry.MechanicalPower.Patches;

/// <summary>Holds a vanilla waterwheel's <c>GearedRatio</c> across its water check. The check
/// writes a ratio of 1 whenever the sensed flow direction changes, which is the first check after
/// every load; when another node seeded the network (a filler port discovers its own), the wheel's
/// true ratio is not 1 and every node on the network then turns at the wrong speed, 5.5 times off
/// across a large gear.</summary>
[HarmonyPatch]
internal static class WaterWheelGearedRatioPatch
{
  // Resolved by name: the method is protected, and a rename in a later game version skips the
  // patch through Prepare instead of throwing during PatchAll.
  private static MethodInfo? Target =>
    AccessTools.Method(
      typeof(BEBehaviorMPWaterWheel),
      "CheckWater",
      [typeof(float)]
    );

  public static bool Prepare() => Target != null;

  public static MethodBase TargetMethod() => Target!;

  public static void Prefix(
    BEBehaviorMPWaterWheel __instance,
    out float __state
  ) => __state = __instance.GearedRatio;

  public static void Postfix(
    BEBehaviorMPWaterWheel __instance,
    float __state
  ) => __instance.GearedRatio = __state;
}
#endif
