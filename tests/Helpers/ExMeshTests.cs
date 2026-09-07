using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExMesh.RotateByShape"/>: rotates a mesh about the cell centre by the block shape's Y
/// rotation, the orientation the chunk tesselator would otherwise bake in.
/// </summary>
public class ExMeshTests {
  private static Block BlockWithRotation(float rotateY) =>
    new() { Shape = new CompositeShape { rotateY = rotateY } };

  [Fact]
  public void A_null_mesh_is_a_no_op() {
    // No exception is the assertion: there is nothing else to observe on a null mesh.
    ExMesh.RotateByShape(null, BlockWithRotation(90));
  }

  [Fact]
  public void A_zero_rotation_leaves_the_mesh_unchanged() {
    var mesh = new MeshData(4);
    mesh.AddVertexSkipTex(1f, 0.5f, 0.5f);

    ExMesh.RotateByShape(mesh, BlockWithRotation(0));

    Assert.Equal(1f, mesh.xyz[0]);
    Assert.Equal(0.5f, mesh.xyz[1]);
    Assert.Equal(0.5f, mesh.xyz[2]);
  }

  [Fact]
  public void A_nonzero_rotation_moves_a_vertex_off_the_rotation_axis() {
    var mesh = new MeshData(4);
    mesh.AddVertexSkipTex(1f, 0.5f, 0.5f); // offset (0.5, 0, 0) from the cell centre

    ExMesh.RotateByShape(mesh, BlockWithRotation(90));

    // Y (the rotation axis) is untouched; X/Z carry the rotation the tesselator applies at
    // placement.
    Assert.Equal(0.5f, mesh.xyz[1], precision: 5);
    Assert.NotEqual(1f, mesh.xyz[0], precision: 5);
  }
}
