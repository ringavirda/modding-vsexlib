using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAssets"/> over every survival blocktype and itemtype
/// file: each file loads or is listed in <c>VanillaSkips</c>, and the reference items resolve.</summary>
[Collection("ItemViews")]
public class WholeVanillaLoadTests(ITestOutputHelper output) {
  private static string Origin(string dir) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Harness",
      "Fixtures",
      dir
    );

  // Fails when a file that does not parse, or has no code, is dropped without a skip row.
  [Fact]
  public void Every_vanilla_type_file_loads_or_is_listed() {
    List<(string File, string Reason)> skips = [];

    List<AssetLocation> added = TestWorld.MirrorVanillaTypes(
      Origin("vanillatypes"),
      ["*"],
      skips
    );

    Assert.Equal(
      ["game:blocktypes/goodblock.json"],
      added.Select(a => a.ToString())
    );
    Assert.Equal(
      ["game:itemtypes/brokenitem.json", "game:itemtypes/codeless.json"],
      skips.Select(s => s.File).Order()
    );
    Assert.Contains(
      "no code",
      skips.Single(s => s.File.EndsWith("codeless.json")).Reason
    );
    Assert.DoesNotContain(
      "no code",
      skips.Single(s => s.File.EndsWith("brokenitem.json")).Reason
    );
  }

  // Fails when a survival type file is neither loaded nor listed.
  [Fact]
  public void The_survival_type_files_are_all_accounted_for() {
    string survival = Path.Combine(
      VsAssemblyResolver.InstallPath!,
      "assets",
      "survival"
    );
    List<(string File, string Reason)> skips = [];

    List<AssetLocation> added = TestWorld.MirrorVanillaTypes(
      survival,
      ["*"],
      skips
    );

    int files = new[] { "blocktypes", "itemtypes" }.Sum(d =>
      Directory
        .EnumerateFiles(
          Path.Combine(survival, d),
          "*.json",
          SearchOption.AllDirectories
        )
        .Count()
    );
    Assert.True(files > 500);
    Assert.Equal(files, added.Count + skips.Count);
    foreach ((string file, string reason) in skips)
      output.WriteLine($"skip {file}: {reason}");
  }

  // Fails when MayDefine's star branch matches nothing.
  [Fact]
  public void The_whole_load_holds_the_reference_items() {
    using var world = new TestWorld();
    var watch = Stopwatch.StartNew();

    world.LoadAssets([], null, ["*"]);

    output.WriteLine($"whole load {watch.Elapsed.TotalSeconds:F1} s");
    foreach ((string file, string reason) in world.VanillaSkips)
      output.WriteLine($"skip {file}: {reason}");
    foreach (
      string code in (string[])
        [
          "spear-generic-iron",
          "oar-crude-aged",
          "ingot-iron",
          "metalbit-iron",
          "metalplate-iron",
          "rod-iron",
          "stick",
          "plank-oak",
        ]
    )
      Assert.True(world.World.GetItem(new AssetLocation(code)) != null, code);
    foreach (
      string code in (string[])
        ["rock-granite", "chute-straight-ns", "bed-wood-head-north"]
    )
      Assert.True(world.World.GetBlock(new AssetLocation(code)) != null, code);
    output.WriteLine(
      $"blocks {world.World.Blocks.Count} items {world.World.Items.Count}"
    );
  }

  // Fails when the object loader's errors are left out of VanillaSkips.
  [Fact]
  public void An_object_loader_error_is_listed_as_a_skip() {
    string modPath = Directory
      .CreateTempSubdirectory("exlib-vanilla-skips-")
      .FullName;
    try {
      File.WriteAllText(
        Path.Combine(modPath, "modinfo.json"),
        """{ "type": "content", "modid": "plantedskip", "version": "1.0.0" }"""
      );
      string blocktypes = Path.Combine(
        modPath,
        "assets",
        "plantedskip",
        "blocktypes"
      );
      Directory.CreateDirectory(blocktypes);
      File.WriteAllText(
        Path.Combine(blocktypes, "planted.json"),
        """{ "code": "planted", "class": "NoSuchPlantedClass" }"""
      );
      using var world = new TestWorld();
      world.Log.Expect(EnumLogType.Error, "NoSuchPlantedClass");

      world.LoadAssets([modPath]);

      Assert.Contains(
        world.VanillaSkips,
        s =>
          s.File == "(object loader)" && s.Reason.Contains("NoSuchPlantedClass")
      );
    } finally {
      Directory.Delete(modPath, recursive: true);
    }
  }
}
