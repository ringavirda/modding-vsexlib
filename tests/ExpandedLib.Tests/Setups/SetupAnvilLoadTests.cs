#if GAME_GE_1_22
using System.IO;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A setup that places the game's anvil loads it as a running server does, and a helve hit <c>call</c> on it runs (setups.md, Loading).</summary>
[Collection("Setups")]
public class SetupAnvilLoadTests
{
  // Fails when the load leaves SurvivalCoreSystem unregistered: BlockAnvil.OnLoaded then throws a null reference as the anvil is placed.
  [Fact]
  public void A_placed_game_anvil_loads_and_takes_a_helve_hit_call()
  {
    using SetupScene scene = SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupmetal\"], \"steps\": ["
          + "{\"fill\": {\"code\": \"game:rock-granite\", \"from\": [0, 0, 0], \"to\": [3, 0, 3]}},"
          + "{\"place\": {\"id\": \"anvil\", \"code\": \"game:anvil-iron\", \"at\": [1, 1, 1]}},"
          + "{\"call\": {\"at\": {\"of\": \"anvil\"}, \"method\": \"OnHelveHammerHit\"}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupMetal"
        ),
      ],
      null,
      _ => { }
    );

    var anvil = Assert.IsType<BlockAnvil>(
      scene.World.World.GetBlock(new AssetLocation("game:anvil-iron"))
    );
    Assert.True(anvil.MetalTier > 0);
  }
}
#endif
