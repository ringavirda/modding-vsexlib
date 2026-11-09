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
    if (
      ExMeshCache.GetOrCreate(
        capi,
        "machinepiece|" + piece.Collectible.Code,
        () => Build(tesselator, piece)
      ) is { } mesh
    )
      mesher.AddMeshData(mesh, transform);
  }

  private static MeshData? Build(ITesselatorAPI tesselator, ItemStack piece) {
    MeshData mesh;
    if (piece.Class == EnumItemClass.Item)
      tesselator.TesselateItem(piece.Item, out mesh);
    else
      tesselator.TesselateBlock(piece.Block, out mesh);
    return mesh;
  }
}
