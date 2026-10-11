using System.Collections.Generic;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="SceneGrid"/>: blank rows at the ends of a layer are trimmed, so its first glyph sits at the origin.</summary>
public class SceneGridTests {
  // Fails when blank leading or trailing rows count as rows (the grid keeps them).
  [Fact]
  public void Blank_rows_at_the_ends_of_a_layer_are_trimmed() {
    var placed = new List<BlockPos>();

    new SceneGrid()
      .On('A', p => placed.Add(p))
      .Layer("\n\nA\n\n", y: 2, originZ: 5);

    Assert.Equal([new BlockPos(0, 2, 5)], placed);
  }
}
