using System.Text;
using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>Variant codes built from the whole code stem, where vanilla's
/// <c>CodeWithVariant</c> keeps only the first dash-segment of a code such as
/// <c>crafting-workbench</c>.</summary>
internal static class ExVariantCodes {
  /// <summary>The code of <paramref name="obj"/> with <paramref name="group"/>'s value replaced by
  /// <paramref name="value"/>; the other groups keep theirs, in <c>Variant</c> order.</summary>
  /// <returns>A copy of the code when <paramref name="obj"/> has no variant groups.</returns>
  internal static AssetLocation WithVariant(
    this RegistryObject obj,
    string group,
    string value
  ) {
    if (obj.Variant == null || obj.Variant.Count == 0)
      return obj.Code.Clone();
    var path = new StringBuilder(obj.CodeWithoutParts(obj.Variant.Count));
    foreach (var part in obj.Variant)
      path.Append('-').Append(part.Key == group ? value : part.Value);
    return new AssetLocation(obj.Code.Domain, path.ToString());
  }
}
