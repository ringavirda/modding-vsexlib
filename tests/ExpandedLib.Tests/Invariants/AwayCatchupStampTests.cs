using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Repo-wide rule: a block entity hosting a production process must round-trip its
/// last-tick stamp, or opt out with <c>no away-catch-up:</c> and a reason.</summary>
public class AwayCatchupStampTests {
  #region Corpus

  // Matches the base-list form: every host declares its process as a nested subclass.
  private static readonly Regex HostsAProcess = new(
    @":\s*BEBehaviorProductionMachine\s*\(",
    RegexOptions.Compiled
  );

  private const string StampKey = "pm_lastHours";
  private const string OptOut = "no away-catch-up:";

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

  private static IReadOnlyList<(string Relative, string Text)> Sources() =>
    [
      .. Premise
        .NotEmpty(SourceFiles(), "mod source files")
        .Select(f => (Rel(f), File.ReadAllText(f))),
    ];

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

  /// <summary>Each file of <paramref name="files"/> that hosts a production process and neither
  /// names the <c>pm_lastHours</c> stamp nor carries the <c>no away-catch-up:</c> opt-out.
  /// </summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns>The offenders' relative paths, in input order.</returns>
  public static IReadOnlyList<string> UnstampedHosts(
    IEnumerable<(string Relative, string Text)> files
  ) =>
    [
      .. files
        .Where(f =>
          HostsAProcess.IsMatch(f.Text)
          && !f.Text.Contains(StampKey, StringComparison.Ordinal)
          && !f.Text.Contains(OptOut, StringComparison.Ordinal)
        )
        .Select(f => f.Relative),
    ];

  /// <summary>Each misplaced or unexplained <c>no away-catch-up:</c> opt-out in
  /// <paramref name="files"/>.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns><c>Stranded</c>: files carrying the marker and hosting no process.
  /// <c>Unexplained</c>: one entry per marker line with under ten characters after it.</returns>
  public static (
    IReadOnlyList<string> Stranded,
    IReadOnlyList<string> Unexplained
  ) MisplacedOptOuts(IEnumerable<(string Relative, string Text)> files) {
    var stranded = new List<string>();
    var unexplained = new List<string>();
    foreach (var (rel, text) in files) {
      if (!text.Contains(OptOut, StringComparison.Ordinal))
        continue;
      if (!HostsAProcess.IsMatch(text))
        stranded.Add(rel);
      foreach (string line in Regex.Split(text, "\r\n|\r|\n")) {
        int at = line.IndexOf(OptOut, StringComparison.Ordinal);
        if (at >= 0 && line[(at + OptOut.Length)..].Trim().Length < 10)
          unexplained.Add(rel);
      }
    }
    return (stranded, unexplained);
  }

  [Fact]
  public void A_process_host_saves_the_stamp_its_catch_up_measures_from() {
    IReadOnlyList<(string Relative, string Text)> files = Sources();
    List<string> offenders = [.. UnstampedHosts(files)];

    Assert.True(
      files.Any(f => HostsAProcess.IsMatch(f.Text)),
      "Found no production-process hosts at all - the source walk is wrong."
    );
    Assert.True(
      offenders.Count == 0,
      "These host a production process but neither save nor restore its \""
        + StampKey
        + "\" stamp, so away-catch-up is dead on them however many steps they allow. Round-trip it "
        + "in ToTreeAttributes/FromTreeAttributes, or mark the file with \""
        + OptOut
        + " <reason>\":\n    "
        + string.Join("\n    ", offenders)
    );
  }

  [Fact]
  public void A_no_catch_up_opt_out_sits_on_a_host_and_states_its_reason() {
    // A stale marker (left after the host moves) exempts whatever host lands in that file next.
    var (stranded, unexplained) = MisplacedOptOuts(Sources());

    // Zero markers is a legitimate outcome: the opt-out is a family machine's escape hatch, not exlib's own.
    Assert.True(
      stranded.Count == 0,
      "These carry the \""
        + OptOut
        + "\" marker but host no production process, so it explains nothing and exempts the file "
        + "from the rule above. Move it to whatever hosts the process now:\n    "
        + string.Join("\n    ", stranded)
    );
    Assert.True(
      unexplained.Count == 0,
      "The opt-out marker must be followed by the reason on the same line:\n    "
        + string.Join("\n    ", unexplained)
    );
  }

  private const string Host =
    "class Kiln : BlockEntity {\n"
    + "  sealed class P() : BEBehaviorProductionMachine(null) { }\n";

  // Fails when UnstampedHosts passes a host with neither stamp nor opt-out, or names a file
  // hosting no process, a stamped host or an opted-out one.
  [Fact]
  [PlantedDefect(typeof(AwayCatchupStampTests), nameof(UnstampedHosts))]
  public void A_host_without_the_stamp_or_an_opt_out_is_named() {
    Assert.Equal(
      ["bare.cs"],
      UnstampedHosts([
        ("bare.cs", Host + "}"),
        ("stamped.cs", Host + "  const string K = \"pm_lastHours\";\n}"),
        ("opted.cs", Host + "  // no away-catch-up: it never runs unloaded\n}"),
        ("plain.cs", "class Chest : BlockEntity { }"),
      ])
    );
  }

  // Fails when MisplacedOptOuts passes a marker on a file hosting no process or one with no
  // reason, or names a reasoned marker on a host.
  [Fact]
  [PlantedDefect(typeof(AwayCatchupStampTests), nameof(MisplacedOptOuts))]
  public void A_stranded_or_unexplained_opt_out_is_named() {
    var (stranded, unexplained) = MisplacedOptOuts([
      ("good.cs", Host + "  // no away-catch-up: it never runs unloaded\n}"),
      ("stranded.cs", "// no away-catch-up: it never runs unloaded"),
      ("terse.cs", Host + "  // no away-catch-up: none\r\n}"),
    ]);

    Assert.Equal(["stranded.cs"], stranded);
    Assert.Equal(["terse.cs"], unexplained);
  }

  #endregion
}
