using ExpandedLib.Helpers;
using HarmonyLib;
using Vintagestory.API.Common;

namespace ExpandedLib.Blocks;

/// <summary>Resolves the snow cover variants of a block with a <c>cover</c> group
/// (<c>notSnowCovered</c>, <c>snowCovered1</c> to <c>snowCovered3</c>, <c>snowLevel</c>) from its
/// whole code, where vanilla's <see cref="Block.OnLoaded"/> builds them from the first
/// dash-segment. A block class that overrides <c>OnLoaded</c> without calling the base escapes
/// it.</summary>
[HarmonyPatch]
internal static class SnowCoverPatch {
  [HarmonyPatch(typeof(Block), nameof(Block.OnLoaded))]
  [HarmonyPostfix]
  public static void FromWholeCode(Block __instance, ICoreAPI api) {
    Block block = __instance;
    if (
      block.Variant?["cover"] is not { } cover
      || (cover != "free" && !cover.Contains("snow"))
    )
      return;
    IWorldAccessor world = api.World;
    block.notSnowCovered = world.GetBlock(block.WithVariant("cover", "free"));
    block.snowCovered1 = world.GetBlock(block.WithVariant("cover", "snow"));
    block.snowCovered2 = world.GetBlock(block.WithVariant("cover", "snow2"));
    block.snowCovered3 = world.GetBlock(block.WithVariant("cover", "snow3"));
    block.snowLevel =
      block == block.snowCovered1 ? 1
      : block == block.snowCovered2 ? 2
      : block == block.snowCovered3 ? 3
      : 0;
  }
}
