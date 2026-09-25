using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The <see cref="Block.OnLoaded"/> postfix that resolves a dashed block's snow cover
/// variants from its whole code: the snow levels, the weather's snowed variant, and
/// <c>BreakSnowFirst</c> clearing a snowed block.</summary>
[Collection(ExHarmonyCollection.Name)]
public class SnowCoverPatchTests {
  private static readonly BlockPos Pos = new(64, 16, 64, 0);

  private static HarmonyFixture Patched() =>
    new("exlibtest.snowcover", typeof(SnowCoverPatch).Assembly);

  private sealed class Path {
    public readonly TestWorld World = new();
    public readonly Block Free;
    public readonly Block Snow;
    public readonly Block Bare;
    public readonly Block Wall;

    public Path() {
      Free = Cover("free", 1);
      Snow = Cover("snow", 2);
      Bare = Cover("bare", 3);
      Wall = TestBlocks.Configure(new Block(), "exlib:slag-wall", 4);
      foreach (Block block in new[] { Free, Snow, Bare, Wall })
        World.Register(block);
      foreach (Block block in new[] { Free, Snow, Bare, Wall })
        block.OnLoaded(World.Api);
    }

    private static Block Cover(string state, int id) =>
      TestBlocks.Configure(
        new Block(),
        $"exlib:slag-path-{state}",
        id,
        ("cover", state)
      );
  }

  // Fails when the postfix builds the codes from the first dash-segment, as vanilla does, or
  // reads no snow level off them.
  [Fact]
  public void A_dashed_block_finds_its_snowed_and_free_variants() {
    using var fixture = Patched();
    var path = new Path();

    Assert.Same(path.Free, path.Snow.notSnowCovered);
    Assert.Same(path.Snow, path.Free.snowCovered1);
    Assert.Equal(1f, path.Snow.snowLevel);
    Assert.Equal(0f, path.Free.snowLevel);
    Assert.Same(path.Snow, path.Free.GetSnowCoveredVariant(Pos, 1));
    Assert.Null(path.Wall.notSnowCovered);
  }

  // Fails when the postfix resolves a cover state that is neither free nor snow, which vanilla
  // leaves without snow variants.
  [Fact]
  public void A_cover_state_neither_free_nor_snowed_gets_no_snow_variants() {
    using var fixture = Patched();
    var path = new Path();

    Assert.Null(path.Bare.notSnowCovered);
    Assert.Null(path.Bare.snowCovered1);
  }

  // Fails when the postfix builds the free variant from the first dash-segment: the snowed block
  // then has none and breaks whole.
  [Fact]
  public void Breaking_a_snowed_dashed_block_clears_its_snow() {
    using var fixture = Patched();
    var path = new Path();
    var breakFirst = new BlockBehaviorBreakSnowFirst(path.Snow);
    path.Snow.BlockBehaviors = [breakFirst];
    path.Snow.CollectibleBehaviors = [breakFirst];
    path.World.Place(Pos, path.Snow);
    TestPlayer player = path.World.Player("breaker");
    player.GameMode = EnumGameMode.Survival;

    path.Snow.OnBlockBroken(path.World.World, Pos, player.Player);

    Assert.Equal(
      "exlib:slag-path-free",
      path.World.GetBlock(Pos).Code.ToString()
    );
  }
}
