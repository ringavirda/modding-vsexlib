using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAssets(System.Collections.Generic.IReadOnlyList{string}, string)"/>
/// over the PatchTarget and PatchSource fixtures: fxsource's patches, applied by the game's own patch
/// loader, add variants to fxtarget's <c>thing</c> and its own <c>other</c>.</summary>
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
    Assert.Contains("successfully applied 1 patches", summary);
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
}
