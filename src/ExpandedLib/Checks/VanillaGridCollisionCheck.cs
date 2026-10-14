using System.Collections.Generic;
using System.Linq;
using static ExpandedLib.Checks.GridRecipeCollisionCheck;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that no grid recipe of a domain matches the same input as a vanilla (<c>game:</c>) grid
/// recipe, read from the loaded game's recipe files, patches applied.
/// </summary>
/// <remarks>Matches as <see cref="GridRecipeCollisionCheck"/> does, a tag-only ingredient taking
/// what the game's tags give it.</remarks>
public static class VanillaGridCollisionCheck {
  /// <summary>Every pair of a grid recipe of <paramref name="domain"/> and a vanilla one that
  /// match the same input.</summary>
  /// <param name="game">The domain's recipes, vanilla's and the loaded tags.</param>
  /// <param name="domain">The domain whose grid recipes are checked; <c>game</c> itself reports
  /// nothing.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>VanillaGridCollision</c>, one error
  /// per colliding pair; no errors when none. Pairs are judged in the hand grid and every grid any
  /// domain declared with <see cref="ExlibChecks.CraftingGrid"/>, so the result depends on those
  /// declarations.</returns>
  public static CheckResult Run(ILoadedGame game, string domain) =>
    domain == "game"
      ? new("VanillaGridCollision", domain, [])
      : Collisions(
        "VanillaGridCollision",
        domain,
        [.. Crafts(game, domain)],
        [.. Crafts(game, "game")],
        ExlibChecks.AllGrids(),
        withinOwn: false
      );
}
