using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAssets(System.Collections.Generic.IReadOnlyList{string}, string)"/>
/// over the PatchTarget and PatchSource fixtures: fxsource's patches, applied by the game's own patch
/// loader, add variants to fxtarget's <c>thing</c>, its own <c>other</c> and vanilla's
/// <c>crushed</c>.</summary>
public class PatchedLoadTests(ITestOutputHelper output) {
  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Harness",
      "Fixtures",
      name
    );

  private static readonly string Target = Fixture("PatchTarget");
  private static readonly string Source = Fixture("PatchSource");
  private static readonly string MissingTarget = Fixture("PatchMissingTarget");

  private static bool Has(TestWorld world, string code) =>
    world.World.GetBlock(new AssetLocation(code)) != null;

  // Fails when the load runs no patch loader: thing keeps its two kinds.
  [Fact]
  public void A_patch_that_depends_on_the_mod_it_patches_adds_its_variant() {
    using var world = new TestWorld();

    world.LoadAssets([Target, Source]);

    Assert.True(Has(world, "fxtarget:thing-c"));
    (EnumLogType type, string summary) = world.Log.Entries.Single(e =>
      e.Message.StartsWith("JsonPatch Loader: ")
    );
    output.WriteLine(summary);
    Assert.Equal(EnumLogType.Notification, type);
    Assert.Contains("successfully applied 3 patches", summary);
  }

  // Fails when each mod's patches apply as that mod loads, before a later mod's assets are in the
  // manager: fxtarget's thing is not there yet when fxsource's patch runs.
  [Fact]
  public void A_patching_mod_listed_first_patches_a_mod_listed_after_it() {
    using var world = new TestWorld();

    world.LoadAssets([Source, Target]);

    Assert.True(Has(world, "fxtarget:thing-c"));
  }

  // Fails when the listed mods join Mods after the patch loader runs (other-x beside fxtarget), and
  // when the single-mod load skips the patch loader (no other-x alone).
  [Fact]
  public void An_inverted_dependency_is_unmet_beside_its_mod_and_met_without_it() {
    using var both = new TestWorld();
    using var alone = new TestWorld();

    both.LoadAssets([Target, Source]);
    alone.LoadAssets(Source);

    Assert.True(Has(both, "fxsource:other-a"));
    Assert.False(Has(both, "fxsource:other-x"));
    Assert.True(Has(alone, "fxsource:other-x"));
  }

  // Fails when the patch loader reads a world config other than Config.Tree.
  [Fact]
  public void A_patch_condition_reads_the_world_config_the_load_starts_with() {
    using var on = new TestWorld();
    using var off = new TestWorld();
    on.Config.Tree.SetString("fxflag", "on");
    off.Config.Tree.SetString("fxflag", "off");

    on.LoadAssets([Target, Source]);
    off.LoadAssets([Target, Source]);

    Assert.True(Has(on, "fxsource:other-y"));
    Assert.False(Has(off, "fxsource:other-y"));
  }

  // Fails when the object loader runs once per listed mod: the first mod's blocks register again
  // under a second id.
  [Fact]
  public void Every_fixture_block_registers_once_under_an_id_of_its_own() {
    using var world = new TestWorld();

    world.LoadAssets([Target, Source]);

    Block[] loaded =
    [
      .. world.World.Blocks.Where(b =>
        b.Code?.Domain is "fxtarget" or "fxsource"
      ),
    ];
    Assert.Equal(
      new[]
      {
        "fxsource:other-a",
        "fxtarget:thing-a",
        "fxtarget:thing-b",
        "fxtarget:thing-c",
      },
      loaded.Select(b => b.Code.ToString()).Order()
    );
    Assert.Equal(
      loaded.Length,
      loaded.Select(b => b.BlockId).Distinct().Count()
    );
    foreach (Block block in loaded)
      Assert.Same(block, world.World.GetBlock(block.BlockId));
  }

  // Fails when the vanilla file a patch names is not mirrored into the load.
  [Fact]
  public void A_patch_into_a_vanilla_file_adds_its_variant() {
    using var world = new TestWorld();

    world.LoadAssets([Target, Source]);

    Assert.NotNull(
      world.World.GetItem(new AssetLocation("game:crushed-fxore"))
    );
  }

  // Fails when every survival itemtype is mirrored, not only the file a patch names. The base game's
  // own blocktypes load with or without a patch, so the patch-free load is the baseline for blocks.
  [Fact]
  public void The_only_vanilla_collectibles_a_patch_adds_are_its_files() {
    using var world = new TestWorld();
    using var unpatched = new TestWorld();
    string[] before = [.. GameItems(world)];

    world.LoadAssets([Target, Source]);
    unpatched.LoadAssets([Target]);

    string[] added = [.. GameItems(world).Except(before)];
    Assert.Contains("game:crushed-fxore", added);
    Assert.All(added, code => Assert.StartsWith("game:crushed-", code));
    Assert.Equal(GameBlocks(unpatched).Order(), GameBlocks(world).Order());
  }

  // Fails when the load throws on a patch target the install lacks (the game: file), and when a
  // target outside game: is read from the install (fxmissing's crushed.json is found there).
  [Fact]
  public void A_patch_into_a_file_the_load_lacks_is_logged() {
    using var world = new TestWorld();
    string[] notFound =
    [
      "File game:itemtypes/fxmissing.json not found",
      "File fxmissing:itemtypes/resource/crushed/crushed.json not found",
    ];
    foreach (string fragment in notFound)
      world.Log.Expect(NotFoundType, fragment);

    world.LoadAssets([MissingTarget]);

    Assert.All(
      notFound,
      fragment =>
        Assert.Contains(
          world.Log.Entries,
          e =>
            e.Type == NotFoundType
            && e.Message.EndsWith(fragment, StringComparison.Ordinal)
        )
    );
  }

  // The game's patch loader logs a missing file as an Error from 1.22, as VerboseDebug before.
#if GAME_GE_1_22
  private const EnumLogType NotFoundType = EnumLogType.Error;
#else
  private const EnumLogType NotFoundType = EnumLogType.VerboseDebug;
#endif

  private static IEnumerable<string> GameItems(TestWorld world) =>
    world
      .World.Items.Where(i => i.Code?.Domain == "game")
      .Select(i => i.Code.ToString());

  private static IEnumerable<string> GameBlocks(TestWorld world) =>
    world
      .World.Blocks.Where(b => b.Code?.Domain == "game")
      .Select(b => b.Code.ToString());
}
