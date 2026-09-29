using System;
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
  /// <remarks>A loaded game's source leaves out the recipes a mod removed after loading
  /// (<see cref="AssetCheckSource.Recipes"/>). An element yielded still a child of its file's
  /// parsed array is numbered by its place among that array's objects, the ones left out
  /// included; any other is numbered by its place among its file's entries.</remarks>
  IEnumerable<(AssetLocation File, JObject Json)> Recipes(string domain);

  /// <summary>Every lang file <paramref name="domain"/> ships, as its locale code (e.g. <c>"en"</c>,
  /// taken from the file name) paired with its parsed JSON.</summary>
  IEnumerable<(string Locale, JObject Json)> Lang(string domain);

  /// <summary>Every code-first block definition <paramref name="domain"/> declares.</summary>
  IEnumerable<ExBlockDef> BlockDefinitions(string domain);

  /// <summary>Every JSON blocktype <paramref name="domain"/> ships, as its file paired with its
  /// parsed object. Code-first definitions arrive through <see cref="BlockDefinitions"/>; a source
  /// that also sees them as blocktype assets may yield them here too. Yields nothing unless the
  /// implementer reads blocktype files.</summary>
  IEnumerable<(AssetLocation File, JObject Json)> BlockTypes(string domain) =>
    [];

  /// <summary>Every JSON itemtype <paramref name="domain"/> ships, as its file paired with its
  /// parsed object, code-first definitions included as their JSON. Yields nothing unless the
  /// implementer reads itemtype files.</summary>
  IEnumerable<(AssetLocation File, JObject Json)> ItemTypes(string domain) =>
    [];

  /// <summary>The type a block <c>class</c> key names, e.g. <c>"iiex.BlockValve"</c>.</summary>
  /// <param name="classKey">The key as a definition's <c>class</c> property writes it.</param>
  /// <returns>Null when this source cannot resolve the key, which is every key unless the
  /// implementer resolves them.</returns>
  Type? BlockClass(string classKey) => null;

  /// <summary>The type a block-entity behaviour key names, as an <c>entityBehaviors</c>
  /// <c>name</c> or a footprint cell's behaviour <c>code</c> writes it.</summary>
  /// <param name="key">The behaviour key.</param>
  /// <returns>Null when this source cannot resolve the key, which is every key unless the
  /// implementer resolves them.</returns>
  Type? BlockEntityBehaviorClass(string key) => null;
}
