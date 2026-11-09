using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>Optional contract for a principal block that reacts when a block next to one of its filler
/// cells changes, such as a structure that breaks when the pit it stands in is altered.</summary>
public interface IFillerNeighbourTarget {
  /// <summary>Called on the server when a block adjacent to a filler cell of this principal changes.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="principalPos">Position of this principal block.</param>
  /// <param name="fillerPos">Position of the filler cell that was notified.</param>
  /// <param name="neighbourPos">Position of the block that changed, adjacent to <paramref name="fillerPos"/>.</param>
  void OnFillerNeighbourChanged(
    IWorldAccessor world,
    BlockPos principalPos,
    BlockPos fillerPos,
    BlockPos neighbourPos
  );
}
