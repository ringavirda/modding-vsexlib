// TwinTubBlower and BurdenMaker only build for the current game version, so a real-asset load of
// them can only run there; the API under test (TestWorld.LoadAssets) itself compiles and runs on
// every game version.
#if GAME_GE_1_22
using System.IO;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="TestWorld.LoadAssets"/> against the two samples: a real block, resolved by the
/// engine's own object loader from each sample's code-first definition, with its variants
/// intact - not a <see cref="TestWorld.RegisterItem"/> stand-in.
/// </summary>
public class AssetLoadingTests
{
  [Fact]
  public void Blower_block_resolves_with_its_orientation_variants()
  {
    string samplePath = Path.Combine(
      RepoPaths.Root,
      "samples",
      "TwinTubBlower"
    );

    using var world = new TestWorld();

    world.LoadAssets(samplePath);

    foreach (string side in new[] { "n", "e", "s", "w" })
    {
      Block? block = world.World.GetBlock(
        new AssetLocation($"twintubblower:blower-twintubblower-{side}")
      );
      Assert.NotNull(block);
      Assert.Equal(
        "TwinTubBlower.Blocks.BlockTwinTubMPBlower",
        block!.GetType().FullName
      );
      Assert.Equal(side, block.Variant["orientation"]);
    }
  }

  [Fact]
  public void Burdenmaker_block_resolves_with_its_side_variants()
  {
    string samplePath = Path.Combine(RepoPaths.Root, "samples", "BurdenMaker");

    using var world = new TestWorld();

    world.LoadAssets(samplePath);

    foreach (string side in new[] { "n", "e", "s", "w" })
    {
      Block? block = world.World.GetBlock(
        new AssetLocation($"burdenmaker:burdenmaker-red-{side}")
      );
      Assert.NotNull(block);
      Assert.Equal(
        "BurdenMaker.Blocks.BlockBurdenmaker",
        block!.GetType().FullName
      );
      Assert.Equal(side, block.Variant["side"]);
    }
  }
}
#endif
