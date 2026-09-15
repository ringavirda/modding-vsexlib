using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Repo-wide rule: a block calling a <see cref="CleanupCalls"/> helper from <c>OnBlockBroken</c>
/// must also call it from <c>OnBlockRemoved(IWorldAccessor, BlockPos)</c>, since
/// <c>Block.OnBlockExploded</c> never calls <c>OnBlockBroken</c>.
/// </summary>
public class FillerCleanupHookTests {
  #region Corpus

  /// <summary>Cleanup calls that must also run from OnBlockRemoved, not just OnBlockBroken.</summary>
  private static readonly string[] CleanupCalls =
  [
    "RemoveFillers(",
    "RemoveAxleNodes(",
  ];

  private static readonly Regex BrokenSignature = new(
    @"override\s+void\s+OnBlockBroken\s*\(",
    RegexOptions.Compiled
  );

  // Matches only the Block overload OnBlockRemoved(IWorldAccessor, BlockPos), not the BlockEntity one.
  private static readonly Regex RemovedBlockSignature = new(
    @"override\s+void\s+OnBlockRemoved\s*\(\s*IWorldAccessor",
    RegexOptions.Compiled
  );

  /// <summary>The brace-matched body of the first method whose signature matches
  /// <paramref name="signature"/>, or null if the signature or its opening brace is not found.</summary>
  private static string? MethodBody(string text, Regex signature) {
    Match m = signature.Match(text);
    if (!m.Success)
      return null;
    int start = text.IndexOf('{', m.Index);
    if (start < 0)
      return null;
    int depth = 0;
    for (int i = start; i < text.Length; i++) {
      if (text[i] == '{')
        depth++;
      else if (text[i] == '}' && --depth == 0)
        return text[start..(i + 1)];
    }
    return null;
  }

  // Every mod's source tree: <mod>/src when present, else the mod's own folder (exlib is flat under src/ExpandedLib).
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

  [Fact]
  public void Filler_cleanup_hangs_off_removal_not_breaking() {
    var offenders = new List<string>();
    int seen = 0;
    foreach (string f in SourceFiles()) {
      string text = File.ReadAllText(f);
      // Matches the call syntax, not a bare mention in another comment.
      bool mentionsAny = false;
      foreach (string call in CleanupCalls) {
        if (text.Contains(call)) {
          mentionsAny = true;
          break;
        }
      }
      if (!mentionsAny)
        continue;
      seen++;

      string? brokenBody = MethodBody(text, BrokenSignature);
      if (brokenBody == null)
        continue;

      string? removedBody = MethodBody(text, RemovedBlockSignature);
      foreach (string call in CleanupCalls) {
        if (!brokenBody.Contains(call))
          continue;
        if (removedBody != null && removedBody.Contains(call))
          continue;
        offenders.Add(Rel(f) + " (" + call.TrimEnd('(') + ")");
        break;
      }
    }

    Assert.True(
      seen > 0,
      "Found no files naming a cleanup call at all - the source walk is wrong."
    );
    Assert.True(
      offenders.Count == 0,
      "these clear their footprint only on a player break: "
        + string.Join(", ", offenders)
    );
  }
}
