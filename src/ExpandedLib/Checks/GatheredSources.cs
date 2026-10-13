using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>The vanilla codes a survival player takes from the world by hand or a tool with no
/// recipe, smelting or firing, and where each comes from.</summary>
/// <remarks>Panned, traded and looted items are not gathered. <see cref="ObtainabilityCheck"/> counts
/// these as made, and a craft walk starts from them.</remarks>
public static class GatheredSources {
  /// <summary>One gathered pattern.</summary>
  /// <param name="Type">Block or item.</param>
  /// <param name="Pattern">The domain-qualified code, a <c>*</c> standing for any states.</param>
  /// <param name="How">Where it comes from and what takes it.</param>
  public sealed record Source(EnumItemClass Type, string Pattern, string How);

  /// <summary>Every gathered pattern, in a fixed order; a new list on each read.</summary>
  public static IReadOnlyList<Source> All =>
    [
      new(
        EnumItemClass.Block,
        "game:rock-*",
        "worldgen, quarried where it lies"
      ),
      new(
        EnumItemClass.Block,
        "game:loosestones-*",
        "worldgen, picked up by hand"
      ),
      new(
        EnumItemClass.Block,
        "game:looseflints-*",
        "worldgen, picked up by hand"
      ),
      new(
        EnumItemClass.Block,
        "game:looseores-*",
        "worldgen, picked up by hand"
      ),
      new(EnumItemClass.Block, "game:ore-*", "worldgen, mined with a pickaxe"),
      new(EnumItemClass.Block, "game:gravel-*", "worldgen, dug where it lies"),
      new(EnumItemClass.Block, "game:sand-*", "worldgen, dug where it lies"),
      new(EnumItemClass.Block, "game:soil-*", "worldgen, dug where it lies"),
      new(EnumItemClass.Block, "game:peat-*", "worldgen, dug where it lies"),
      new(EnumItemClass.Block, "game:rawclay-*", "worldgen, dug where it lies"),
      new(EnumItemClass.Block, "game:log-*", "felled trees, cut with an axe"),
      new(
        EnumItemClass.Block,
        "game:water-*",
        "a water source, taken with a bucket"
      ),
      new(
        EnumItemClass.Item,
        "game:stick",
        "fallen branches and leaves, picked up or broken"
      ),
      new(
        EnumItemClass.Item,
        "game:stone-*",
        "loose stones, picked up by hand"
      ),
      new(EnumItemClass.Item, "game:flint", "loose flint, picked up by hand"),
      new(EnumItemClass.Item, "game:clay-*", "dug clay, taken by hand"),
      new(EnumItemClass.Item, "game:drygrass", "tall grass, cut with a knife"),
      new(
        EnumItemClass.Item,
        "game:bone",
        "creature drops, skinned or butchered"
      ),
      new(
        EnumItemClass.Item,
        "game:hide-*",
        "creature drops, skinned or butchered"
      ),
      new(
        EnumItemClass.Item,
        "game:fat",
        "creature drops, skinned or butchered"
      ),
      new(
        EnumItemClass.Item,
        "game:feather",
        "creature drops, skinned or butchered"
      ),
    ];
}
