using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A machine reads its tooling and names no product in code. See docs/design/mechanics/process-extension.md.</summary>
public class ProcessExtensionGuards {
  #region Corpus

  private sealed record SourceFile(string Relative, string Text);

  // A file is a process machine when it reads a process registry.
  private static readonly Regex ReadsARegistry = new(
    @"ProcessRouteRegistry|ProcessJobRegistry|ProcessExtensions|MillSchedule|RollSetSpec|MoldSpec",
    RegexOptions.Compiled
  );

  // A literal code is banned; an art path is not.
  private static readonly Regex LiteralCode = new(
    """new\s+AssetLocation\s*\(\s*"(?<code>[a-z][a-z0-9]*:[^"]*)"\s*\)""",
    RegexOptions.Compiled
  );

  private static IReadOnlyList<SourceFile> ProcessMachines() {
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
          // The registries and spec types name the contract itself.
          || rel.Contains("/Processes/", StringComparison.Ordinal)
        )
          continue;

        string text = File.ReadAllText(path);
        if (ReadsARegistry.IsMatch(text))
          files.Add(new SourceFile(rel, text));
      }
    }
    return files;
  }

  private static bool IsArtPath(string code) =>
    code.Contains("shapes/", StringComparison.OrdinalIgnoreCase)
    || code.Contains("textures/", StringComparison.OrdinalIgnoreCase)
    || code.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

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
  public void The_corpus_is_not_empty() {
    IReadOnlyList<SourceFile> machines = ProcessMachines();

    Assert.True(
      machines.Count > 0,
      "Found no process-machine sources under src/ - the corpus rule is wrong and the guard below is "
        + "checking nothing."
    );
  }

  [Fact]
  public void No_process_machine_names_a_product_in_code() {
    string[] offenders =
    [
      .. ProcessMachines()
        .SelectMany(f =>
          LiteralCode
            .Matches(f.Text)
            .Where(m => !IsArtPath(m.Groups["code"].Value))
            .Select(m =>
              $"{f.Relative}:{LineOf(f.Text, m.Index)} names \"{m.Groups["code"].Value}\""
            )
        ),
    ];

    Assert.True(
      offenders.Length == 0,
      "A machine reads its tooling and names no product. Declare it in the machine's registry instead:\n  "
        + string.Join("\n  ", offenders)
    );
  }

  #endregion
}
