using System.Collections.Generic;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// What a check reads: the codes, files and definitions of one or more domains, from the game's
/// loaded assets (<see cref="AssetCheckSource"/>) or a repository source tree. A check never
/// touches a registry, file system or assembly directly, only through this interface.
/// </summary>
public interface ICheckSource {
  /// <summary>The mod domains this source covers.</summary>
  IEnumerable<string> Domains { get; }

  /// <summary>Every concrete block code registered across <see cref="Domains"/>.</summary>
  IEnumerable<AssetLocation> BlockCodes { get; }

  /// <summary>Every concrete item code registered across <see cref="Domains"/>.</summary>
  IEnumerable<AssetLocation> ItemCodes { get; }

  /// <summary>Every recipe <paramref name="domain"/> ships, one entry per recipe object; a
  /// multi-recipe file yields one entry per element.</summary>
  IEnumerable<(AssetLocation File, JObject Json)> Recipes(string domain);

  /// <summary>Every lang file <paramref name="domain"/> ships, as its locale code (e.g. <c>"en"</c>,
  /// taken from the file name) paired with its parsed JSON.</summary>
  IEnumerable<(string Locale, JObject Json)> Lang(string domain);

  /// <summary>Every code-first block definition <paramref name="domain"/> declares.</summary>
  IEnumerable<ExBlockDef> BlockDefinitions(string domain);
}
