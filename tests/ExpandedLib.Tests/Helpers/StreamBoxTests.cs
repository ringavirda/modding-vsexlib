using ExpandedLib.Helpers;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="StreamRenderer.StreamBox"/>: the box a pour draws, from the slope foot's stream
/// (x 7.25-8.75, z 8-9, lip 17.83 down to the basin metal at 14.4 in the north frame).
/// </summary>
public class StreamBoxTests {
  private static readonly Vec3f Lip = new(8f, 17.83f, 8.5f);

  // Fails when the rotation sign is flipped: 90 and 270 trade places.
  [Theory]
  [InlineData(0f, 7.25f, 8f, 8.75f, 9f)]
  [InlineData(90f, 8f, 7.25f, 9f, 8.75f)]
  [InlineData(180f, 7.25f, 7f, 8.75f, 8f)]
  [InlineData(270f, 7f, 7.25f, 8f, 8.75f)]
  public void StreamBox_TurnsWithBlock(
    float rotation,
    float x1,
    float z1,
    float x2,
    float z2
  ) {
    Cuboidf box = StreamRenderer.StreamBox(Lip, 14.4f, 1.5f, 1f, rotation);

    Assert.Equal(x1, box.X1, 3);
    Assert.Equal(z1, box.Z1, 3);
    Assert.Equal(x2, box.X2, 3);
    Assert.Equal(z2, box.Z2, 3);
    Assert.Equal(14.4f, box.Y1, 3);
    Assert.Equal(17.83f, box.Y2, 3);
  }

  // Fails when width and depth swap: x would span the depth.
  [Fact]
  public void StreamBox_WidthAndDepth() {
    Cuboidf box = StreamRenderer.StreamBox(Lip, 14.4f, 3f, 1f, 0f);

    Assert.Equal(3f, box.X2 - box.X1, 3);
    Assert.Equal(1f, box.Z2 - box.Z1, 3);
  }
}
