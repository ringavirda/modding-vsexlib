using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>A cap overlay on one face: the north-end cap drawing turned about the cell centre.</summary>
/// <param name="Face">The capped face.</param>
/// <param name="RotateXDeg">Degrees about X.</param>
/// <param name="RotateYDeg">Degrees about Y.</param>
internal readonly record struct PipeCapOverlay(
  BlockFacing Face,
  int RotateXDeg,
  int RotateYDeg
);

/// <summary>The cap overlays a pipe piece draws, a pure function of the faces that carry a cap.</summary>
internal static class PipeCapOverlays {
  /// <summary>The overlay of each capped face, in face-index order.</summary>
  /// <param name="cappedFaces">The faces of the piece that carry a cap; empty for none, repeats ignored.</param>
  /// <returns>One overlay per distinct face; empty when no face is capped.</returns>
  internal static IReadOnlyList<PipeCapOverlay> For(
    IEnumerable<BlockFacing> cappedFaces
  ) =>
    [
      .. cappedFaces
        .DistinctBy(f => f.Index)
        .OrderBy(f => f.Index)
        .Select(Pose),
    ];

  /// <summary>The mesh key of a capped-face set: one bit per face index, so 0 for no cap and at most 63.</summary>
  /// <param name="cappedFaces">The faces of the piece that carry a cap.</param>
  internal static int Key(IEnumerable<BlockFacing> cappedFaces) =>
    cappedFaces.Aggregate(0, (key, face) => key | 1 << face.Index);

  // The cap drawing stands on a north end; the other faces turn it about the cell centre.
  private static PipeCapOverlay Pose(BlockFacing face) =>
    face.Index switch {
      BlockFacing.indexSOUTH => new(face, 0, 180),
      BlockFacing.indexWEST => new(face, 0, 90),
      BlockFacing.indexEAST => new(face, 0, 270),
      BlockFacing.indexUP => new(face, 90, 0),
      BlockFacing.indexDOWN => new(face, 270, 0),
      _ => new(face, 0, 0),
    };
}
