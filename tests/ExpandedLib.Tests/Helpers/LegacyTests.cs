// Compiles only on game-version floors lacking the real member; empty on 1.22.
// LegacyAnimUtil.CreateMesh and LegacyApi120's MeshAngle need a live client and are not covered.
#if !GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Legacy;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

public class LegacyTests {
  #region LegacyLinq.Index

  [Fact]
  public void Index_pairs_each_element_with_its_zero_based_position() {
    IEnumerable<string> source = ["a", "b", "c"];

    var indexed = source.Index().ToList();

    Assert.Equal([(0, "a"), (1, "b"), (2, "c")], indexed);
  }

  [Fact]
  public void Index_over_an_empty_sequence_yields_nothing() {
    Assert.Empty(Enumerable.Empty<string>().Index());
  }

  #endregion

  #region LegacyApi120.ToRandomItemstackForPlayer (1.20 only: guarded further by !GAME_GE_1_21)

#if !GAME_GE_1_21
  [Fact]
  public void ToRandomItemstackForPlayer_falls_back_to_GetNextItemStack() {
    // A fixed (zero-variance) quantity makes GetNextItemStack's roll deterministic.
    var drop = new BlockDropItemStack(new ItemStack(new Item()), 1f) {
      Quantity = NatFloat.createUniform(2f, 0f),
    };

    ItemStack? viaShim = drop.ToRandomItemstackForPlayer(null!, null!);
    ItemStack? direct = drop.GetNextItemStack(1f);

    Assert.NotNull(viaShim);
    Assert.Equal(direct!.StackSize, viaShim!.StackSize);
  }
#endif

  #endregion
}
#endif
