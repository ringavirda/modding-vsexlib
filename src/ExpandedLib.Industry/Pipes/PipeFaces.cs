using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>Sorts the neighbours of a pipe node's faces.</summary>
public static class PipeFaces {
  /// <summary>The class of the neighbour across <paramref name="face"/> of <paramref name="node"/>, tried in the order vent, air, structure, solid, other. Reads only, mutates nothing.</summary>
  /// <param name="blockAccessor">Reads the neighbour block.</param>
  /// <param name="vent">The strategy that serves the node, as <see cref="PipeNetwork.VentFor"/> gives it; <c>null</c> skips the vent class.</param>
  /// <param name="node">The node block at <paramref name="nodePos"/>.</param>
  /// <param name="nodePos">The node's position.</param>
  /// <param name="face">The face of the node whose neighbour is classified.</param>
  public static PipeFaceClass Classify(
    IBlockAccessor blockAccessor,
    IPipeVentStrategy? vent,
    BlockNetworkNode node,
    BlockPos nodePos,
    BlockFacing face
  ) {
    BlockPos neighbourPos = nodePos.AddCopy(face);
    Block neighbour = blockAccessor.GetBlock(neighbourPos);
    if (
      vent != null
      && vent.TryClassifyVent(
        blockAccessor,
        node,
        nodePos,
        face,
        neighbour,
        out _
      )
    )
      return PipeFaceClass.Vent;
    if (neighbour.FirstCodePart() == "air")
      return PipeFaceClass.Air;
    if (neighbour is BlockStructureFiller or IFillerHost)
      return PipeFaceClass.Structure;
    return NetworkFaces.ClosesOpening(
      blockAccessor,
      neighbourPos,
      face.Opposite
    )
      ? PipeFaceClass.Solid
      : PipeFaceClass.Other;
  }
}
