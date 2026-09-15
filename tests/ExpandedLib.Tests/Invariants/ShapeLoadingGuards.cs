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

  // Whole-file matching: the formatter breaks a fluent chain across lines.
  private static string[] Offenders(Regex pattern) =>
    [
      .. Production()
        .SelectMany(f =>
          pattern
            .Matches(f.Text)
            .Select(m => $"{f.Relative}:{LineOf(f.Text, m.Index)}")
        ),
    ];

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
    string[] offenders = Offenders(new Regex(@"ToObject\s*<\s*Shape\s*>\s*\("));

    Assert.True(
      offenders.Length == 0,
      "Load shapes through ExMeshCache.LoadShape (Shape.TryGet), not a raw ToObject<Shape>():\n  "
        + string.Join("\n  ", offenders)
    );
  }

  [Fact]
  public void A_blocktypes_own_shape_path_is_never_rewritten_in_place() {
    // WithPathPrefixOnce/WithPathAppendixOnce mutate the receiver: calling either on Block.Shape.Base
    // permanently rewrites the path for every instance of that blocktype.
    string[] offenders = Offenders(
      new Regex(@"Shape\s*\.\s*Base\s*\.\s*WithPath")
    );

    Assert.True(
      offenders.Length == 0,
      "Clone the location before prefixing it (ExMeshCache.ShapePathOf):\n  "
        + string.Join("\n  ", offenders)
    );
  }

  #endregion
}
