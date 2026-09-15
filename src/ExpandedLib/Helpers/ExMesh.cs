using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>Small shared helpers for hand-tesselated block meshes.</summary>
public static class ExMesh {
  /// <summary>Rotates <paramref name="mesh"/> in place about the cell centre by the block shape's Y rotation.</summary>
  public static void RotateByShape(MeshData? mesh, Block block) {
    if (mesh == null)
      return;
    float rotY = block.Shape.rotateY * GameMath.DEG2RAD;
    if (rotY != 0f)
      mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotY, 0);
  }
}
