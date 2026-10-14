using ExpandedLib.Industry.Pipes;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>What the <see cref="LiveFixture"/> world holds for the layout walk to place.</summary>
public class LiveFixtureTests {
  // Fails when the fixture loads game:stone only.
  [Fact]
  public void The_world_holds_the_blower_a_plated_pipe_and_stone() {
    var fixture = LiveFixture.WithSamples();

    Block blower = fixture.Test.World.GetBlock(
      new AssetLocation("twintubblower:blower-twintubblower-n")
    );
    Block pipe = fixture.Test.World.GetBlock(
      new AssetLocation("platedpipes:pipe-plated-straight-we")
    );

    Assert.Equal("blower-twintubblower-n", blower.Code.Path);
    Assert.IsType<BlockPipe>(pipe);
    Assert.Same(
      fixture.Stone,
      fixture.Test.World.GetBlock(new AssetLocation("game:stone"))
    );
  }
}
