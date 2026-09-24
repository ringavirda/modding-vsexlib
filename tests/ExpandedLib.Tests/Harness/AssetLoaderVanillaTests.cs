using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAssets"/> over the base game alone: a content mod with no
/// assets of its own, and one whose block names a class nothing registers.</summary>
public class AssetLoaderVanillaTests : IDisposable {
  private readonly string _modPath = Directory
    .CreateTempSubdirectory("exlib-asset-loader-test-")
    .FullName;

  public void Dispose() => Directory.Delete(_modPath, recursive: true);

  private void WriteContentMod(string modId) =>
    File.WriteAllText(
      Path.Combine(_modPath, "modinfo.json"),
      $$"""{ "type": "content", "modid": "{{modId}}", "version": "1.0.0" }"""
    );

  // Fails when LoadAssets starts no vanilla mod system or leaves the tag converters unset.
  [Fact]
  public void A_vanilla_only_load_logs_no_warning_or_error() {
    WriteContentMod("vanillaonly");
    using var world = new TestWorld();

    world.LoadAssets(_modPath);

    Assert.Empty(world.Log.Warnings);
    Assert.Empty(world.Log.Errors);
    Assert.Equal(
      "BlockMultiblock",
      world
        .World.GetBlock(new AssetLocation("game:multiblock-monolithic-0-0-p1"))
        ?.GetType()
        .Name
    );
  }

  // Fails when a vanilla system starts throwing against the loader's API, or stops: a class it
  // registers below the throw would be lost unseen.
  [Fact]
  public void The_vanilla_systems_passed_over_are_the_known_four() {
    WriteContentMod("passedover");
    using var world = new TestWorld();

    world.LoadAssets(_modPath);

    Assert.Equal(
      [
        "Vintagestory.GameContent.ModSystemEntityOwnership",
        "Vintagestory.GameContent.ModSystemOreMap",
        "Vintagestory.GameContent.RecipeRegistrySystem",
        "Vintagestory.GameContent.StoryStructuresSpawnConditions",
      ],
      world.PassedOverVanillaSystems.Order()
    );
  }

  // Fails when the loader's API logs anywhere but the world's Log, so an unknown class goes unseen.
  [Fact]
  public void A_block_naming_an_unregistered_class_still_logs_its_error() {
    WriteContentMod("plantedclass");
    string blocktypes = Path.Combine(
      _modPath,
      "assets",
      "plantedclass",
      "blocktypes"
    );
    Directory.CreateDirectory(blocktypes);
    File.WriteAllText(
      Path.Combine(blocktypes, "planted.json"),
      """{ "code": "planted", "class": "NoSuchPlantedClass" }"""
    );
    using var world = new TestWorld();
    world.Log.Expect(EnumLogType.Error, "NoSuchPlantedClass");

    world.LoadAssets(_modPath);

    Assert.Single(
      world.Log.Errors,
      e =>
        e.Contains("plantedclass:planted")
        && e.Contains("NoSuchPlantedClass, no such class registered")
    );
    Assert.Single(world.Log.Errors);
  }
}
