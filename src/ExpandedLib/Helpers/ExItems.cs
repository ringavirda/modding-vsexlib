using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>Shared item-stack lookups for interaction help, cached in the API's <c>ObjectCache</c>.</summary>
public static class ExItems {
  private const string WrenchCacheKey = "exlib:wrenchStacks";

  /// <summary>Returns one stack per registered wrench item, for rotate and repair interaction-help icons.</summary>
  public static ItemStack[] WrenchStacks(IWorldAccessor world) {
    if (
      world.Api.ObjectCache.TryGetValue(WrenchCacheKey, out object? cached)
      && cached is ItemStack[] stacks
    )
      return stacks;

    ItemStack[] built =
    [
      .. world
        .Items.Where(i => i.Code?.Path?.Contains("wrench") == true)
        .Select(i => new ItemStack(i)),
    ];
    world.Api.ObjectCache[WrenchCacheKey] = built;
    return built;
  }
}
