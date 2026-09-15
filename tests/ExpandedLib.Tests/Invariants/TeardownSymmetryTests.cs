using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Whatever cleans up in <c>OnBlockRemoved</c> must also clean up in
/// <c>OnBlockUnloaded</c>, or state in the file why not, via a <c>removal-only teardown:</c>
/// marker.</summary>
public class TeardownSymmetryTests {
  #region Corpus

  // The block entity signatures take no arguments, unlike the Block overloads of the same names.
  private static readonly Regex RemovedOverride = new(
    @"override\s+void\s+OnBlockRemoved\s*\(\s*\)",
    RegexOptions.Compiled
  );
  private static readonly Regex UnloadedOverride = new(
    @"override\s+void\s+OnBlockUnloaded\s*\(\s*\)",
    RegexOptions.Compiled
  );

  private const string OptOut = "removal-only teardown:";

  // Every mod's own source tree: <mod>/src when that folder holds it, else the mod's own folder.
  private static IEnumerable<string> SourceFiles() {
    foreach (string mod in RepoManifest.Mods.Values) {
      string full = Path.Combine(mod, "src");
      if (!Directory.Exists(full))
        full = mod;
      foreach (
        string path in Directory.EnumerateFiles(
          full,
          "*.cs",
          SearchOption.AllDirectories
        )
      ) {
        string rel = Rel(path);
        if (
          rel.Contains("/bin/", StringComparison.Ordinal)
          || rel.Contains("/obj/", StringComparison.Ordinal)
          || path.EndsWith(".g.cs", StringComparison.Ordinal)
        )
          continue;
        yield return path;
      }
    }
  }

  private static string Rel(string path) =>
    Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

  private static string RepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (
      dir != null && !File.Exists(Path.Combine(dir.FullName, "ExpandedLib.sln"))
    )
      dir = dir.Parent;
    return dir?.FullName
      ?? throw new InvalidOperationException(
        "Could not locate the repo root (ExpandedLib.sln) from "
          + AppContext.BaseDirectory
      );
  }

  #endregion

  #region Rules

  [Fact]
  public void A_block_entity_that_tears_down_on_removal_also_tears_down_on_unload() {
    var offenders = new List<string>();
    int seen = 0;
    foreach (string f in SourceFiles()) {
      string text = File.ReadAllText(f);
      if (!RemovedOverride.IsMatch(text))
        continue;
      seen++;
      if (UnloadedOverride.IsMatch(text))
        continue;
      if (text.Contains(OptOut, StringComparison.Ordinal))
        continue;
      offenders.Add(Rel(f));
    }

    Assert.True(
      seen > 0,
      "Found no OnBlockRemoved overrides at all - the source walk is wrong."
    );
    Assert.True(
      offenders.Count == 0,
      "These clean up on removal but not on chunk unload. Add an OnBlockUnloaded doing the "
        + "client-side share of the teardown, or mark the file with \""
        + OptOut
        + " <reason>\":\n    "
        + string.Join("\n    ", offenders)
    );
  }

  [Fact]
  public void A_removal_only_opt_out_sits_where_the_removal_teardown_does() {
    var offenders = new List<string>();
    int marked = 0;
    foreach (string f in SourceFiles()) {
      string text = File.ReadAllText(f);
      if (!text.Contains(OptOut, StringComparison.Ordinal))
        continue;
      marked++;
      if (!RemovedOverride.IsMatch(text))
        offenders.Add(Rel(f));
    }

    Assert.True(
      marked > 0,
      "Found no opt-out markers at all - the source walk is wrong."
    );
    Assert.True(
      offenders.Count == 0,
      "These carry the \""
        + OptOut
        + "\" marker but override no OnBlockRemoved(), so it explains nothing and exempts the "
        + "file from the rule above. Move it to whatever does the removal-only teardown now:\n    "
        + string.Join("\n    ", offenders)
    );
  }

  [Fact]
  public void A_removal_only_opt_out_states_its_reason() {
    var offenders = new List<string>();
    foreach (string f in SourceFiles()) {
      foreach (string line in File.ReadAllLines(f)) {
        int at = line.IndexOf(OptOut, StringComparison.Ordinal);
        if (at < 0)
          continue;
        if (line[(at + OptOut.Length)..].Trim().Length < 10)
          offenders.Add(Rel(f));
      }
    }

    Assert.True(
      offenders.Count == 0,
      "The opt-out marker must be followed by the reason on the same line:\n    "
        + string.Join("\n    ", offenders)
    );
  }

  #endregion
}
