using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// What a loaded check reads beyond <see cref="ICheckSource"/>: every block and item the game
/// registered, what its recipe registries make, and the tags it resolved, patches applied. Only a
/// loaded game answers it (<see cref="AssetCheckSource"/>): a repository or assembly source has no
/// vanilla content and no patches.
/// </summary>
public interface ILoadedGame : ICheckSource {
  /// <summary>Every block and item registered, of every domain, as loaded: codes, variant groups,
  /// drops, smelted, crushed and ground stacks, creative tabs.</summary>
  IEnumerable<CollectibleObject> Collectibles { get; }

  /// <summary>What every recipe registry makes: each grid recipe's output, a cooking recipe's
  /// <c>cooksInto</c>, and the output of each barrel, alloy, smithing, knapping and clayforming
  /// recipe.</summary>
  IEnumerable<LoadedOutput> RecipeOutputs { get; }

  /// <summary>The codes a recipe ingredient that names tags and no code takes: every collectible
  /// of its <c>type</c> whose tags meet its tag condition.</summary>
  /// <param name="ingredient">The ingredient's JSON as a recipe file writes it.</param>
  /// <returns>The codes taken, empty when none; null when the ingredient names a code or no tags,
  /// or the game has no tags (before 1.22).</returns>
  IReadOnlyList<AssetLocation>? Tagged(JObject ingredient);
}
