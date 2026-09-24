using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>What a loaded check reads beyond <see cref="ICheckSource"/>: every block and item the
/// game registered, what its registries make, and the tags it resolved, patches applied.</summary>
/// <remarks>Only a loaded game answers it (<see cref="AssetCheckSource"/>): a repository or
/// assembly source has no vanilla content and no patches.</remarks>
public interface ILoadedGame : ICheckSource {
  /// <summary>Every block and item registered, of every domain, as loaded: codes, variant groups,
  /// drops, smelted, crushed and ground stacks, creative tabs.</summary>
  IEnumerable<CollectibleObject> Collectibles { get; }

  /// <summary>What every recipe registry and exlib process catalogue makes.</summary>
  /// <remarks>Each grid recipe's output, a cooking recipe's <c>cooksInto</c>, the output of each
  /// barrel, alloy, smithing, knapping and clayforming recipe, each terminal job's output
  /// (<see cref="Catalogues.ProcessJobRegistry"/>), each stock route's stopping point
  /// (<see cref="Catalogues.ProcessRouteRegistry"/>), and each loaded die's job output
  /// (<see cref="Catalogues.ItemDie"/>).</remarks>
  IEnumerable<LoadedOutput> RecipeOutputs { get; }

  /// <summary>The codes a recipe ingredient that names tags and no code takes: every collectible
  /// of its <c>type</c> whose tags meet its tag condition.</summary>
  /// <param name="ingredient">The ingredient's JSON as a recipe file writes it.</param>
  /// <returns>The codes taken, empty when none; null when the ingredient names a code or no tags,
  /// or the game has no tags (before 1.22).</returns>
  IReadOnlyList<AssetLocation>? Tagged(JObject ingredient);
}
