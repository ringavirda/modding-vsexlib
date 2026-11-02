using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace SashWeights.Tests;

/// <summary>
/// The weight's drawn size against the units the pattern pays: 1 x 1 x 6 voxels is 6 voxel cubes, 15 units at 2.5 a cube.
/// </summary>
public class WeightShapeTests {
  [Fact]
  public void Weight_is_one_by_one_by_six_voxels() {
    string shape = Path.Combine(
      RepoPaths.Assets("sashweights"),
      "shapes",
      "item",
      "sashweight.json"
    );

    (float width, float thickness, float length) = ShapeExtents.Of(shape);

    Assert.Equal((1f, 1f, 6f), (width, thickness, length));
  }
}
