using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Industry.Casting;

/// <summary>
/// Classifies tool moulds by where they can be cast: small moulds sit on a mould pedestal, oversize moulds
/// are cast at a station that takes them. A mould's <c>moldFit</c> block attribute decides first
/// (<c>"oversize"</c> or <c>"pedestal"</c>); without it the mould is oversize when its <c>tooltype</c>
/// variant is in <see cref="MoldFitRegistry"/>. Only a vanilla <see cref="BlockToolMold"/> qualifies, as
/// only it carries the pour contract.
/// </summary>
public static class MoldFits {
  /// <summary>The block attribute that states a mould's fit.</summary>
  public const string AttributeKey = "moldFit";

  /// <summary>Whether <paramref name="block"/> is a tool mould too large for a pedestal. False for null and for any
  /// block that is not a <see cref="BlockToolMold"/>.</summary>
  public static bool IsOversize(Block? block) =>
    IsOversize(block, MoldFitRegistry.Shared);

  /// <summary>Whether <paramref name="block"/> is a tool mould that fits a pedestal: a
  /// <see cref="BlockToolMold"/> that is not oversize. False for null and for any other block.</summary>
  public static bool FitsPedestal(Block? block) =>
    FitsPedestal(block, MoldFitRegistry.Shared);

  internal static bool IsOversize(Block? block, MoldFitRegistry registry) {
    if (block is not BlockToolMold)
      return false;
    return block.Attributes?[AttributeKey].AsString() switch {
      "oversize" => true,
      "pedestal" => false,
      _ => registry.IsOversizeType(block.Variant?["tooltype"]),
    };
  }

  internal static bool FitsPedestal(Block? block, MoldFitRegistry registry) =>
    block is BlockToolMold && !IsOversize(block, registry);
}
