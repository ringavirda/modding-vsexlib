using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>A mega-block whose footprint cells are declared by its <c>fillerOffsets</c> attribute node.</summary>
public interface IFillerHost {
  /// <summary>The block's <c>fillerOffsets</c> JSON node, or null if none.</summary>
  JsonObject? FillerOffsets { get; }
}
