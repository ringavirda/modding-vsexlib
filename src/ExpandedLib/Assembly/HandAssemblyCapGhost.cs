using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The uploaded ghost meshes of caps about to be fitted, one per piece block and end, kept until disposed. Client only, used from the render thread.</summary>
internal sealed class HandAssemblyCapGhost : IDisposable {
  private readonly Dictionary<
    (int Block, int Face),
    MultiTextureMeshRef?
  > _kept = [];

  /// <summary>The ghost of a cap on the end <paramref name="face"/> of <paramref name="piece"/>: the piece's own <see cref="ICapOverlayMesh"/> overlay of that end, uploaded on first use.</summary>
  /// <param name="capi">The client API, to mesh and upload.</param>
  /// <param name="piece">The piece's block.</param>
  /// <param name="face">The end the cap goes on.</param>
  /// <returns>Null when the piece draws no cap overlay.</returns>
  public MultiTextureMeshRef? Of(
    ICoreClientAPI capi,
    Block piece,
    BlockFacing face
  ) {
    if (
      _kept.TryGetValue((piece.Id, face.Index), out MultiTextureMeshRef? kept)
    )
      return kept;
    MeshData? mesh = (piece as ICapOverlayMesh)?.CapOverlay(capi, face);
    MultiTextureMeshRef? uploaded =
      mesh == null ? null : capi.Render.UploadMultiTextureMesh(mesh);
    _kept[(piece.Id, face.Index)] = uploaded;
    return uploaded;
  }

  /// <summary>Disposes every uploaded ghost.</summary>
  public void Dispose() {
    foreach (MultiTextureMeshRef? kept in _kept.Values)
      kept?.Dispose();
    _kept.Clear();
  }
}
