using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace Grains;

/// <summary>
/// Builds one code-first sack item per grain - the linen sack shape, the way
/// <c>HandMill</c>'s blocks use vanilla mechanics art rather than authoring a mesh.
/// Pure: no asset reads, so it is unit-testable without a
/// <see cref="Vintagestory.API.Common.ICoreAPI"/>.
/// </summary>
public static class GrainSackItems {
  /// <summary>One <see cref="ExItemDef"/> per <paramref name="grains"/> entry, coded
  /// <c>sack-&lt;code&gt;</c> in <paramref name="domain"/>. The linen sack shape carries its own
  /// textures; no <c>Texture</c> call.</summary>
  public static IEnumerable<ExItemDef> Emit(
    string domain,
    IEnumerable<GrainDef> grains
  ) {
    foreach (GrainDef grain in grains)
      yield return ExItemDef
        .Create(domain, "sack-" + grain.Code)
        .Shape("game:item/bag/linensack")
        .MaxStackSize(16)
        .CreativeCommon("*");
  }
}
