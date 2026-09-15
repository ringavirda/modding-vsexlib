using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Helpers;

/// <summary>
/// Composes block display names that include the block's material variant: metal, rock, brick, and
/// any variant group registered through <see cref="AddVariantQualifier"/>. The qualifier is always a
/// parenthetical suffix, never a prefix.
/// </summary>
public static class ExBlockNames {
  // Registration order is application order; a duplicate group replaces its prefix in place.
  private static readonly List<(string Group, string LangPrefix)> _qualifiers =
  [];

  /// <summary>The registered variant groups, in the order <see cref="Decorate"/> applies them.</summary>
  internal static IReadOnlyList<(string Group, string LangPrefix)> Qualifiers =>
    _qualifiers;

  /// <summary>Registers a variant group so <see cref="Decorate"/> also qualifies on it.</summary>
  /// <param name="variantGroup">The block variant key to test (e.g. <c>"refractory"</c>).</param>
  /// <param name="langPrefix">Prepended to the variant value to form the lang key looked up.</param>
  public static void AddVariantQualifier(string variantGroup, string langPrefix) {
    for (int i = 0; i < _qualifiers.Count; i++) {
      if (_qualifiers[i].Group == variantGroup) {
        _qualifiers[i] = (variantGroup, langPrefix);
        return;
      }
    }
    _qualifiers.Add((variantGroup, langPrefix));
  }

  /// <summary>Removes a registered variant group, if any; internal, test-only teardown.</summary>
  internal static void RemoveVariantQualifier(string variantGroup) =>
    _qualifiers.RemoveAll(q => q.Group == variantGroup);

  /// <summary>Decorates <paramref name="baseName"/> with the recognised variant values of
  /// <paramref name="block"/>: metal, rock, brick, then every group added through
  /// <see cref="AddVariantQualifier"/>.</summary>
  public static string Decorate(Block block, string baseName) {
    string name = baseName;

    string? material = block.Variant["material"];
    string? rock = block.Variant["rock"];
    string? brick = block.Variant["brick"];

    if (material != null)
      name = AppendQualifier(name, Lang.Get("material-" + material));
    else if (rock != null)
      name = AppendQualifier(name, Lang.Get("rock-" + rock));
    else if (brick != null)
      name = AppendQualifier(
        name,
        Lang.GetWithFallback(
          block.Code.Domain + ":brickname-" + brick,
          "exlib:brickname-" + brick
        )
      );

    foreach ((string group, string langPrefix) in _qualifiers) {
      string? value = block.Variant[group];
      if (value != null)
        name = AppendQualifier(name, Lang.Get(langPrefix + value));
    }

    return name;
  }

  /// <summary>Appends <paramref name="qualifier"/> to <paramref name="name"/> as a parenthetical
  /// suffix, merged into an existing group.</summary>
  private static string AppendQualifier(string name, string qualifier) {
    if (name.EndsWith(')') && name.Contains('('))
      return name[..^1] + Lang.Get("exlib:blockname-listsep") + qualifier + ")";
    return Lang.Get("exlib:blockname-suffixed", name, qualifier);
  }
}
