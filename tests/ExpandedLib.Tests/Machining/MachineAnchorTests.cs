using System.Numerics;
using ExpandedLib.Industry.Machining;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The matrix that places a mounted piece's mesh at a machine anchor.</summary>
public sealed class MachineAnchorTests {
  private static readonly Vec3f NoTurn = new(0f, 0f, 0f);

  private static Vec3f Map(float[] m, float x, float y, float z) =>
    new(
      m[0] * x + m[4] * y + m[8] * z + m[12],
      m[1] * x + m[5] * y + m[9] * z + m[13],
      m[2] * x + m[6] * y + m[10] * z + m[14]
    );

  private static MachineAnchor Anchor(
    Vector3 origin,
    Vector3? rotation = null,
    float scale = 1f
  ) => new("table", origin, rotation ?? Vector3.Zero, scale);

  // Fails when the anchor's origin is left out of the matrix: the piece's centre then stays at the cell centre.
  [Fact]
  public void The_pieces_centre_lands_at_the_anchors_origin() {
    float[] m = MachineAnchors.Compose(Anchor(new(4f, 12f, 8f)), NoTurn);

    Vec3f centre = Map(m, 0.5f, 0.5f, 0.5f);

    Assert.Equal(0.25f, centre.X, 4);
    Assert.Equal(0.75f, centre.Y, 4);
    Assert.Equal(0.5f, centre.Z, 4);
  }

  // Fails when the anchor's rotation is dropped or turned about the wrong axis.
  [Fact]
  public void The_anchors_rotation_turns_the_piece_about_its_centre() {
    float[] m = MachineAnchors.Compose(
      Anchor(new(8f, 8f, 8f), new Vector3(0f, 90f, 0f)),
      NoTurn
    );

    Vec3f edge = Map(m, 1f, 0.5f, 0.5f);

    Assert.Equal(0.5f, edge.X, 4);
    Assert.Equal(0.5f, edge.Y, 4);
    Assert.Equal(0f, edge.Z, 4);
  }

  // Fails when the anchor's scale is dropped or applied about the cell origin instead of the piece's centre.
  [Fact]
  public void The_anchors_scale_shrinks_the_piece_about_its_centre() {
    float[] m = MachineAnchors.Compose(
      Anchor(new(8f, 8f, 8f), null, 0.5f),
      NoTurn
    );

    Vec3f edge = Map(m, 1f, 0.5f, 0.5f);
    Vec3f centre = Map(m, 0.5f, 0.5f, 0.5f);

    Assert.Equal(0.75f, edge.X, 4);
    Assert.Equal(0.5f, centre.X, 4);
  }

  // Fails when the block's facing is not applied to the anchor.
  [Theory]
  [InlineData(0f, 0.375f, 0.75f)]
  [InlineData(90f, 0.75f, 0.625f)]
  [InlineData(180f, 0.625f, 0.25f)]
  [InlineData(270f, 0.25f, 0.375f)]
  public void The_blocks_facing_carries_the_anchor_about_the_block_centre(
    float rotateY,
    float x,
    float z
  ) {
    float[] m = MachineAnchors.Compose(
      Anchor(new(6f, 10f, 12f)),
      new Vec3f(0f, rotateY, 0f)
    );

    Vec3f centre = Map(m, 0.5f, 0.5f, 0.5f);

    Assert.Equal(x, centre.X, 4);
    Assert.Equal(0.625f, centre.Y, 4);
    Assert.Equal(z, centre.Z, 4);
  }
}
