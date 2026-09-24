// TwinTubBlower and BurdenMaker build only for the current game version.
#if GAME_GE_1_22
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Blocks;
using ExpandedLib.Config;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using HarmonyLib;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAssets"/> against the two samples: a real block resolved by
/// the engine's own object loader, with its variants intact.</summary>
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

  private static string BreakFixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Harness",
      "Fixtures",
      "BreakFixture"
    );

  // Fails when the loader leaves every block at id 0 (the last one loaded replaces air), or an item
  // at id 0.
  [Fact]
  public void Loaded_blocks_and_items_take_ids_of_their_own()
  {
    using var world = new TestWorld();

    world.LoadAssets(BreakFixture);

    Assert.Equal("game:air", world.World.GetBlock(0).Code.ToString());
    int[] ids = [.. world.World.Blocks.Select(b => b.BlockId)];
    Assert.Equal(ids.Length, ids.Distinct().Count());
    Assert.Contains(
      world.World.Blocks,
      b => b.Code.ToString() == "breakfixture:frame-n"
    );
    Assert.NotEqual(
      0,
      world.GetItem(new AssetLocation("breakfixture:token"))!.ItemId
    );
  }

  // Fails when the loader registers classes into a registry of its own, so a block set in the world
  // spawns no entity.
  [Fact]
  public void A_loaded_block_set_in_the_world_spawns_its_entity_with_its_behaviours()
  {
    using var world = new TestWorld();
    world.LoadAssets(BreakFixture);
    var at = new Vintagestory.API.MathTools.BlockPos(0, 64, 0);

    world.Accessor.SetBlock(
      world.World.GetBlock(new AssetLocation("breakfixture:frame-n"))!.BlockId,
      at
    );

    Assert.NotNull(
      world.GetBlockEntity(at)?.GetBehavior<ExRightClickConstructable>()
    );
  }

  // Fails when the loader stops starting exlib's own mod systems, so no exlib class is registered.
  [Fact]
  public void A_mod_block_naming_an_exlib_class_resolves_to_it()
  {
    using var world = new TestWorld();

    world.LoadAssets(Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"));

    Assert.IsType<BlockPipe>(
      world.World.GetBlock(
        new AssetLocation("platedpipes:pipe-plated-straight-ns")
      )
    );
  }

  // Fails when the loader disposes exlib's systems and leaves the mod's own started.
  [Fact]
  public void A_load_disposes_the_mods_own_systems()
  {
    using var world = new TestWorld();

    world.LoadAssets(Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"));

    ModSystem plated = world.LoadedSystems.Single(s =>
      s.GetType().Name == "PlatedPipesModSystem"
    );
    Assert.Null(
      typeof(ExModSystem)
        .GetField("_modules", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(plated)
    );
  }

  // Fails when the loader leaves exlib's systems undisposed, so their Harmony patches outlive the load.
  [Fact]
  public void A_load_leaves_no_exlib_harmony_patch()
  {
    using var world = new TestWorld();

    world.LoadAssets(Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"));

    Assert.DoesNotContain(
      Harmony.GetAllPatchedMethods(),
      m => Harmony.GetPatchInfo(m)?.Owners.Contains("exlib") ?? false
    );
  }

  // Fails when LoadAssets keeps the code-first definitions an earlier load registered.
  [Fact]
  public void A_second_load_meets_no_block_of_the_first()
  {
    using (var first = new TestWorld())
      first.LoadAssets(
        Path.Combine(RepoPaths.Root, "samples", "TwinTubBlower")
      );
    using var second = new TestWorld();

    second.LoadAssets(Path.Combine(RepoPaths.Root, "samples", "BurdenMaker"));

    Assert.Null(
      second.World.GetBlock(
        new AssetLocation("twintubblower:blower-twintubblower-n")
      )
    );
    Assert.DoesNotContain(second.Log.Errors, e => e.Contains("twintubblower"));
  }

  // World A registers the metal and the preference as a mod's Start would; the sample ships none.
  [Fact]
  public void A_world_loaded_after_another_holds_nothing_of_a_mod_it_lacks()
  {
    var platedPipe = new BlockPipe();
    platedPipe.VariantStrict["tier"] = BlockPipe.PlatedTier;
    platedPipe.Variant = new(platedPipe.VariantStrict);
    using (var first = new TestWorld())
    {
      first.LoadAssets(Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"));
      MetalRegistry.Register(
        new MetalDef { Code = "platedsteel", MoltenItem = "platedpipes:molten" }
      );
      var preference = Substitute.For<IExPreference>();
      preference.Key.Returns("platedpipes-units");
      ExPreferences.Register(preference);

      Assert.Equal(2.5f, platedPipe.BurstPressure);
      Assert.Contains(
        ExDefinitions.Blocks,
        d => d.Location.Domain == "platedpipes"
      );
      Assert.Contains(
        ExDefinitions.Recipes,
        d => d.Location.Domain == "platedpipes"
      );
      Assert.True(ExConfigProfiles.TryGet("platedpipes", out _));
      Assert.True(MetalRegistry.TryGet("platedpipes:molten", out _));
      Assert.NotNull(ExPreferences.Find("platedpipes-units"));
    }
    using var second = new TestWorld();

    second.LoadAssets(Path.Combine(RepoPaths.Root, "samples", "BurdenMaker"));

    Assert.DoesNotContain(
      ExDefinitions.Blocks,
      d => d.Location.Domain == "platedpipes"
    );
    Assert.DoesNotContain(
      ExDefinitions.Items,
      d => d.Location.Domain == "platedpipes"
    );
    Assert.DoesNotContain(
      ExDefinitions.Recipes,
      d => d.Location.Domain == "platedpipes"
    );
    Assert.DoesNotContain(
      second.World.Blocks,
      b => b.Code?.Domain == "platedpipes"
    );
    Assert.False(MetalRegistry.TryGet("platedpipes:molten", out _));
    Assert.False(ExConfigProfiles.TryGet("platedpipes", out _));
    Assert.Null(ExPreferences.Find("platedpipes-units"));
    Assert.NotEqual(2.5f, platedPipe.BurstPressure);
    Assert.Empty(second.Log.Warnings);
    Assert.Empty(second.Log.Errors);
  }
}
#endif
