using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>A piece that draws a fitted cap as an overlay mesh of its own; hand assembly shows the same mesh as the ghost of a cap about to be fitted.</summary>
internal interface ICapOverlayMesh {
  /// <summary>The overlay mesh of a cap on the end <paramref name="face"/>, in block space from 0 to 1. Client only.</summary>
  /// <param name="capi">The client API, to read and mesh the cap shape.</param>
  /// <param name="face">The end the cap sits on.</param>
  /// <returns>Null when the piece ships no cap shape.</returns>
  MeshData? CapOverlay(ICoreClientAPI capi, BlockFacing face);
}
