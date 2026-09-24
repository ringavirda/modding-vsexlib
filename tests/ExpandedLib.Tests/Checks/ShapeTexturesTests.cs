using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ShapeTextures"/> over planted values and shape folders.</summary>
public class ShapeTexturesTests {
  /// <summary>Fails if the leading-slash, drive-letter or <c>wsl.localhost</c> alternative is
  /// dropped from the pattern; each rejected input matches that one alternative alone.</summary>
  [Theory]
  [PlantedDefect(typeof(ShapeTextures), nameof(ShapeTextures.IsEditorPath))]
  [InlineData("/home/fallen/workbench/cast-iron1")]
  [InlineData("C:/Users/fallen/cast-iron1")]
  [InlineData(@"d:\art\cast-iron1")]
  [InlineData("wsl.localhost/archlinux/cast-iron1")]
  public void An_editor_path_is_rejected(string value) =>
    Assert.True(ShapeTextures.IsEditorPath(value));

  /// <summary>Fails if the pattern matches an asset location, domained or bare.</summary>
  [Theory]
  [InlineData("iiex:block/metal/castiron")]
  [InlineData("game:block/metal/ingot/{metal}")]
  [InlineData("block/clay/brick/four/running/fire1")]
  public void An_asset_location_is_accepted(string value) =>
    Assert.False(ShapeTextures.IsEditorPath(value));

  // Fails when EditorPaths skips a nested folder, reads an element's own textures map, or names an
  // asset location.
  [Fact]
  [PlantedDefect(typeof(ShapeTextures), nameof(ShapeTextures.EditorPaths))]
  public void An_editor_path_in_a_nested_shape_is_named() {
    using var shapes = new PlantedFiles();
    shapes.Write(
      "a.json",
      """{ "textures": { "ok": "game:block/metal/iron", "n": 3 } }"""
    );
    shapes.Write(
      "boiler/b.json",
      """{ "textures": { "coal": "C:/art/coal", "ok": "block/coal" } }"""
    );
    shapes.Write(
      "c.json",
      """{ "elements": [ { "textures": { "x": "/home/x" } } ] }"""
    );

    Assert.Equal(
      [
        $"boiler{System.IO.Path.DirectorySeparatorChar}b.json: coal = C:/art/coal",
      ],
      ShapeTextures.EditorPaths(shapes.Root)
    );
  }
}
