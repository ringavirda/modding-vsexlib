using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Machining;

/// <summary>Client side of a machine tool's mounted piece: its item mesh, cached per item code, added to the chunk.</summary>
internal static class MachineToolRenderer {
  internal static void AddPiece(
    ICoreClientAPI capi,
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator,
    ItemStack piece,
    float[] transform
  ) {
    if (PieceMesh(capi, tesselator, piece) is { } mesh)
      mesher.AddMeshData(mesh, transform);
  }

  /// <summary>Whether a machine of <paramref name="kind"/> draws its mounted piece through a
  /// <see cref="MountedPieceRenderer"/>: its kind declares a spindle.</summary>
  internal static bool TakesRenderer(MachineKind? kind) =>
    kind?.Spindle is not null;

  /// <summary>Whether a machine of <paramref name="kind"/> adds its mounted piece to the chunk mesh.</summary>
  internal static bool DrawsInChunk(MachineKind? kind) => !TakesRenderer(kind);

  /// <summary>The piece's item mesh in its own unit cell, cached per item code; null when it cannot be tesselated.</summary>
  internal static MeshData? PieceMesh(
    ICoreClientAPI capi,
    ITesselatorAPI tesselator,
    ItemStack piece
  ) =>
    ExMeshCache.GetOrCreate(
      capi,
      "machinepiece|" + piece.Collectible.Code,
      () => Build(tesselator, piece)
    );

  private static MeshData? Build(ITesselatorAPI tesselator, ItemStack piece) {
    MeshData mesh;
    if (piece.Class == EnumItemClass.Item)
      tesselator.TesselateItem(piece.Item, out mesh);
    else
      tesselator.TesselateBlock(piece.Block, out mesh);
    return mesh;
  }
}
