using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>How shape assets may be loaded, enforced by a source-text scan across every mod.</summary>
public class ShapeLoadingGuards {
  #region Corpus

  private sealed record SourceFile(string Relative, string Text);

  private static IReadOnlyList<SourceFile> Production() {
    string root = RepoRoot();
    var files = new List<SourceFile>();

    foreach (string mod in RepoManifest.Mods.Values) {
      string src = Path.Combine(mod, "src");
      if (!Directory.Exists(src))
        src = mod;

      foreach (
        string path in Directory.EnumerateFiles(
          src,
          "*.cs",
          SearchOption.AllDirectories
        )
      ) {
        string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (
          rel.Contains("/bin/", StringComparison.Ordinal)
          || rel.Contains("/obj/", StringComparison.Ordinal)
          || path.EndsWith(".g.cs", StringComparison.Ordinal)
        )
          continue;
        files.Add(new SourceFile(rel, File.ReadAllText(path)));
      }
    }

    Assert.True(
      files.Count > 0,
      "Found no C# sources under mods/*/src - the repo-root walk is wrong, and every rule below would "
        + "pass by scanning nothing."
    );
    return files;
  }

  private static readonly Regex RawShapeRead = new(
    @"ToObject\s*<\s*Shape\s*>\s*\("
  );
  private static readonly Regex ShapePathRewrite = new(
    @"Shape\s*\.\s*Base\s*\.\s*WithPath"
  );

  // Whole-file matching: the formatter breaks a fluent chain across lines.
  private static IReadOnlyList<string> Offenders(
    Regex pattern,
    IEnumerable<(string Relative, string Text)> files
  ) =>
    [
      .. files.SelectMany(f =>
        pattern
          .Matches(f.Text)
          .Select(m => $"{f.Relative}:{LineOf(f.Text, m.Index)}")
      ),
    ];

  private static IReadOnlyList<(string Relative, string Text)> Corpus() =>
    [
      .. Premise
        .NotEmpty(Production(), "production sources")
        .Select(f => (f.Relative, f.Text)),
    ];

  /// <summary>Each <c>ToObject&lt;Shape&gt;(</c> call in <paramref name="files"/>, spaced or split
  /// across lines.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns><c>path:line</c> per call, in input order.</returns>
  public static IReadOnlyList<string> RawShapeReads(
    IEnumerable<(string Relative, string Text)> files
  ) => Offenders(RawShapeRead, files);

  /// <summary>Each <c>Shape.Base.WithPath...</c> call in <paramref name="files"/>, which rewrites a
  /// blocktype's own shape path in place.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns><c>path:line</c> per call, in input order.</returns>
  public static IReadOnlyList<string> InPlaceShapePaths(
    IEnumerable<(string Relative, string Text)> files
  ) => Offenders(ShapePathRewrite, files);

  private static int LineOf(string text, int index) =>
    text.Take(index).Count(c => c == '\n') + 1;

  private static string RepoRoot() {
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (
      dir != null && !File.Exists(Path.Combine(dir.FullName, "ExpandedLib.sln"))
    )
      dir = dir.Parent;
    Assert.True(dir != null, "could not locate repo root (ExpandedLib.sln)");
    return dir!.FullName;
  }

  #endregion

  #region Rules

  [Fact]
  public void No_shape_is_deserialized_straight_off_the_asset() {
    // ToObject<Shape>() throws on a malformed shape file with no offending path in the log.
    IReadOnlyList<string> offenders = RawShapeReads(Corpus());

    Assert.True(
      offenders.Count == 0,
      "Load shapes through ExMeshCache.LoadShape (Shape.TryGet), not a raw ToObject<Shape>():\n  "
        + string.Join("\n  ", offenders)
    );
  }

  [Fact]
  public void A_blocktypes_own_shape_path_is_never_rewritten_in_place() {
    // WithPathPrefixOnce/WithPathAppendixOnce mutate the receiver: calling either on Block.Shape.Base
    // permanently rewrites the path for every instance of that blocktype.
    IReadOnlyList<string> offenders = InPlaceShapePaths(Corpus());

    Assert.True(
      offenders.Count == 0,
      "Clone the location before prefixing it (ExMeshCache.ShapePathOf):\n  "
        + string.Join("\n  ", offenders)
    );
  }

  // Fails when RawShapeReads misses a spaced or line-split ToObject<Shape>( or names another type.
  [Fact]
  [PlantedDefect(typeof(ShapeLoadingGuards), nameof(RawShapeReads))]
  public void A_raw_shape_read_is_named() {
    Assert.Equal(
      ["a.cs:1", "a.cs:4"],
      RawShapeReads([
        (
          "a.cs",
          "var s = asset.ToObject< Shape >();\n"
            + "var t = asset.ToObject<ShapeElement>();\n"
            + "var u = asset\n  .ToObject<Shape>\n  ();"
        ),
      ])
    );
  }

  // Fails when InPlaceShapePaths misses a line-split rewrite of Shape.Base or names a clone's.
  [Fact]
  [PlantedDefect(typeof(ShapeLoadingGuards), nameof(InPlaceShapePaths))]
  public void An_in_place_shape_path_rewrite_is_named() {
    Assert.Equal(
      ["a.cs:2"],
      InPlaceShapePaths([
        (
          "a.cs",
          "var p = Shape.Base.Clone().WithPathPrefixOnce(\"shapes/\");\n"
            + "Shape\n  .Base\n  .WithPathAppendixOnce(\".json\");"
        ),
      ])
    );
  }

  #endregion
}
