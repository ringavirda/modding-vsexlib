using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ExpandedLib.Industry.Molten;

/// <summary>Gives <see cref="ItemCrowbar.GetHeldTpUseAnimation"/> the crowbar's own use animation, as on a
/// support beam, while a player aims at an <see cref="ICrowbarVerb"/> block that would act; vanilla plays
/// <c>interactstatic</c> on every other block.</summary>
[HarmonyPatch]
internal static class CrowbarUseAnimationPatch {
  [HarmonyPatch(typeof(ItemCrowbar), nameof(ItemCrowbar.GetHeldTpUseAnimation))]
  [HarmonyPostfix]
  public static void Animation(
    ItemCrowbar __instance,
    Entity forEntity,
    ref string __result
  ) {
    if (
      forEntity is EntityPlayer { BlockSelection: { } sel }
      && forEntity.World.BlockAccessor.GetBlock(sel.Position)
        is ICrowbarVerb verb
      && verb.CrowbarActs(forEntity.World.BlockAccessor, sel.Position)
    )
      __result = __instance.HeldTpUseAnimation;
  }
}
