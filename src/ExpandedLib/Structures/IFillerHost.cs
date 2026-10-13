using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>A mega-block whose footprint cells are declared by its <c>fillerOffsets</c> attribute node.</summary>
public interface IFillerHost {
  /// <summary>The block's <c>fillerOffsets</c> JSON node, or null if none.</summary>
  JsonObject? FillerOffsets { get; }

  /// <summary>The turn in degrees (0, 90, 180 or 270) that takes the north-frame <c>fillerOffsets</c>
  /// to this variant's placement. Unless overridden, a <see cref="Block"/> reads its <c>side</c>
  /// variant, or its <c>orientation</c> variant without one, through
  /// <see cref="ExOrientation.AngleFromSide"/>; any other host, and a block with neither, is 0.</summary>
  int StructureAngle =>
    this is Block block
      ? ExOrientation.AngleFromSide(
        block.Variant?["side"] ?? block.Variant?["orientation"]
      )
      : 0;
}
