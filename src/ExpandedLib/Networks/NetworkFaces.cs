using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>What a neighbouring cell does to an opening of a network node, read the same by every network and by the parts that fit a cap to an opening.</summary>
public static class NetworkFaces {
  /// <summary>Whether the block at <paramref name="neighbourPos"/> shuts an opening that faces it: its whole face toward the opening is solid, by <see cref="Block.SideIsSolid(IBlockAccessor, BlockPos, int)"/>, so a chiselled block answers per face. A structure cell never shuts one: a <see cref="BlockStructureFiller"/> and any block that implements <see cref="IFillerHost"/> report every face solid without covering it. Reads only; air and an unloaded cell answer <c>false</c>.</summary>
  /// <param name="blockAccessor">Reads the neighbour block.</param>
  /// <param name="neighbourPos">The cell the opening faces.</param>
  /// <param name="faceTowardPipe">The face of the neighbour that points back at the opening, so the opening's face opposite.</param>
  public static bool ClosesOpening(
    IBlockAccessor blockAccessor,
    BlockPos neighbourPos,
    BlockFacing faceTowardPipe
  ) {
    Block block = blockAccessor.GetBlock(neighbourPos);
    return block.Id != 0
      && block is not (BlockStructureFiller or IFillerHost)
      && block.SideIsSolid(blockAccessor, neighbourPos, faceTowardPipe.Index);
  }
}
