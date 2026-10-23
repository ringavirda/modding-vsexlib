#if GAME_GE_1_22
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAsGame"/> and <see cref="TestWorld.LoadAssets(System.Collections.Generic.IReadOnlyList{string}, string)"/>
/// load a listed mod's <c>modinfo.json</c> dependencies before it, as the game does.</summary>
[Collection("ItemViews")]
public class DependencyLoadingTests
{
  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Harness",
      "Fixtures",
      name
    );

  private static readonly string DependsOnExlib = Fixture("DependsOnExlib");
  private static readonly string Base = Fixture("DependencyBase");
  private static readonly string Top = Fixture("DependencyTop");

  public DependencyLoadingTests() => RepoPaths.Register("fxdepbase", Base);

  // Fails when the dependency step is removed: exlib's own assets are not in the manager.
  [Fact]
  public void A_mod_that_depends_on_exlib_loads_exlibs_assets()
  {
    using var world = new TestWorld();

    world.LoadAsGame([DependsOnExlib]);

    Assert.NotNull(
      world.Api.Assets.TryGet(
        new AssetLocation("exlib:shapes/pipe/passthrough.json")
      )
    );
  }

  // Fails when the dependency step is removed: fxdepbase is not read before fxdeptop.
  [Fact]
  public void An_unlisted_dependency_is_read_before_its_dependent()
  {
    var mods = TestWorld.ReadWithDependencies([Top]);

    Assert.Equal(["fxdepbase", "fxdeptop"], mods.Select(m => m.Id));
  }

  // Fails when a listed dependency is not recognised as listed: fxdepbase is read twice.
  [Fact]
  public void A_dependency_that_is_listed_is_read_once()
  {
    var mods = TestWorld.ReadWithDependencies([Base, Top]);

    Assert.Equal(["fxdepbase", "fxdeptop"], mods.Select(m => m.Id));
  }

  // Fails when the metadata names another folder than the exlib mod beside the harness.
  [Fact]
  public void The_harness_names_the_exlib_mod_it_was_built_beside()
  {
    string expected = Path.GetFullPath(
      Path.Combine(RepoPaths.Root, "src", "ExpandedLib")
    );

    Assert.Equal(
      expected.TrimEnd(Path.DirectorySeparatorChar),
      RepoPaths.ExlibSource?.TrimEnd(Path.DirectorySeparatorChar)
    );
  }

  // Fails when the exlib fallback is removed: a consumer's repository holds no exlib assets and the list lacks exlib.
  [Fact]
  public void A_consumer_repository_without_exlib_assets_loads_exlib_from_the_source_tree()
  {
    var mods = TestWorld.ReadWithDependencies(
      [DependsOnExlib],
      id =>
        Path.Combine(Path.GetTempPath(), "fxnowhere", "mods", id, "assets", id)
    );

    string exlibPath = mods.Single(m => m.Id == "exlib").Path;
    Assert.True(
      File.Exists(
        Path.Combine(
          exlibPath,
          "assets",
          "exlib",
          "patches",
          "player-clips.json"
        )
      )
    );
  }

  // Fails when a dependency folder that cannot be read throws instead of being skipped.
  [Fact]
  public void A_dependency_folder_without_a_modinfo_is_skipped()
  {
    string bare = Path.Combine(
      Path.GetTempPath(),
      "fxdepbare-" + Path.GetRandomFileName()
    );
    Directory.CreateDirectory(Path.Combine(bare, "assets", "fxdepbase"));
    RepoPaths.Register("fxdepbase", bare);
    try
    {
      var mods = TestWorld.ReadWithDependencies([Top]);

      Assert.Equal(["fxdeptop"], mods.Select(m => m.Id));
    }
    finally
    {
      RepoPaths.Register("fxdepbase", Base);
      Directory.Delete(bare, true);
    }
  }
}
#endif
