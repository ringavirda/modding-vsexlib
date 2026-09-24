using System;
using System.Text;
using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>Variant codes built from the whole code stem, where vanilla's
/// <c>CodeWithVariant</c> and <c>CodeWithVariants</c> keep only the first dash-segment of a code
/// such as <c>crafting-workbench</c>.</summary>
public static class ExVariantCodes {
  /// <summary>The code of <paramref name="obj"/> with <paramref name="group"/>'s value replaced by
  /// <paramref name="value"/>; the other groups keep theirs, in <c>Variant</c> order.</summary>
  /// <param name="obj">A block or item with its code and variant groups loaded.</param>
  /// <param name="group">The variant group to replace; one the object does not carry changes
  /// nothing.</param>
  /// <param name="value">The group's new state.</param>
  /// <returns>The code in <paramref name="obj"/>'s domain, which no block need carry; a copy of the
  /// code when <paramref name="obj"/> has no variant groups.</returns>
  public static AssetLocation WithVariant(
    this RegistryObject obj,
    string group,
    string value
  ) => obj.WithVariants([group], [value]);

  /// <summary>The code of <paramref name="obj"/> with each of <paramref name="groups"/>' values
  /// replaced by the value at the same index of <paramref name="values"/>; a group the object does
  /// not carry is ignored, the other groups keep theirs, in <c>Variant</c> order.</summary>
  /// <param name="obj">A block or item with its code and variant groups loaded.</param>
  /// <param name="groups">The variant groups to replace.</param>
  /// <param name="values">The new states, one per entry of <paramref name="groups"/>.</param>
  /// <returns>The code in <paramref name="obj"/>'s domain, which no block need carry; a copy of the
  /// code when <paramref name="obj"/> has no variant groups.</returns>
  /// <exception cref="IndexOutOfRangeException"><paramref name="values"/> is shorter than
  /// <paramref name="groups"/> and the object carries a group past its end.</exception>
  public static AssetLocation WithVariants(
    this RegistryObject obj,
    string[] groups,
    string[] values
  ) {
    if (obj.Variant == null || obj.Variant.Count == 0)
      return obj.Code.Clone();
    var path = new StringBuilder(obj.CodeWithoutParts(obj.Variant.Count));
    foreach (var part in obj.Variant) {
      int index = Array.IndexOf(groups, part.Key);
      path.Append('-').Append(index >= 0 ? values[index] : part.Value);
    }
    return new AssetLocation(obj.Code.Domain, path.ToString());
  }
}
