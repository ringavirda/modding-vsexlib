using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that no loaded collectible carries null where vanilla dereferences a collection of every
/// collectible: <c>CreativeInventoryTabs</c> (<c>BehaviorAttachable</c>, looking at a mount) and
/// a block's <c>Variant</c> (<c>Snowballs</c>, at load).
/// </summary>
/// <remarks>The game's loader fills both for a mod's collectibles, so a null is the mod's code
/// clearing one after load. Vanilla's own air carries no creative tabs and is not read.</remarks>
public static class CollectibleCollectionsCheck {
  /// <summary>Every collectible of <paramref name="domain"/> with a null collection vanilla
  /// dereferences.</summary>
  /// <param name="game">The loaded collectibles.</param>
  /// <param name="domain">The domain whose collectibles are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>CollectibleCollections</c>, one error
  /// per null collection; no errors when none.</returns>
  public static CheckResult Run(ILoadedGame game, string domain) {
    var errors = new List<string>();
    foreach (var c in game.Collectibles.Where(c => c.Code.Domain == domain)) {
      if (c.CreativeInventoryTabs == null)
        errors.Add(
          $"{c.Code}: CreativeInventoryTabs is null, which looking at a mount dereferences"
        );
      if (c.ItemClass == EnumItemClass.Block && c.Variant == null)
        errors.Add($"{c.Code}: Variant is null, which the snowball system dereferences");
    }
    return new CheckResult("CollectibleCollections", domain, errors);
  }
}
