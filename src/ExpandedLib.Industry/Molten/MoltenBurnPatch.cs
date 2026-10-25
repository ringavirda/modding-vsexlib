using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>Routes <see cref="Block.OnEntityCollide"/> and <see cref="Block.GetTraversalCost"/> of a
/// block carrying <see cref="BlockBehaviorMoltenBurn"/> to <see cref="MoltenBurn"/>; vanilla's block
/// behaviours have neither hook.</summary>
[HarmonyPatch]
internal static class MoltenBurnPatch {
  [HarmonyPatch(typeof(Block), nameof(Block.OnEntityCollide))]
  [HarmonyPostfix]
  public static void Collide(
    Block __instance,
    IWorldAccessor world,
    Entity entity,
    BlockPos pos,
    BlockFacing facing
  ) =>
    __instance
      .GetBehavior<BlockBehaviorMoltenBurn>()
      ?.Collide(world, entity, pos, facing);

  [HarmonyPatch(typeof(Block), nameof(Block.GetTraversalCost))]
  [HarmonyPostfix]
  public static void Traversal(
    Block __instance,
    BlockPos pos,
    EnumAICreatureType creatureType,
    ref float __result
  ) {
    if (__instance.GetBehavior<BlockBehaviorMoltenBurn>() != null)
      __result = MoltenBurn.TraversalCost(
        __instance.GetBlockEntity<BlockEntity>(pos),
        creatureType,
        __result
      );
  }
}
