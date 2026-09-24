using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

  private static IReadOnlyList<(string Relative, string Text)> Sources() =>
    [
      .. Premise
        .NotEmpty(SourceFiles(), "mod source files")
        .Select(f => (Rel(f), File.ReadAllText(f))),
    ];

  #region Rules

  /// <summary>Each file of <paramref name="files"/> overriding the block entity's
  /// <c>OnBlockRemoved()</c> with no <c>OnBlockUnloaded()</c> and no
  /// <c>removal-only teardown:</c> marker.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns>The offenders' relative paths, in input order.</returns>
  public static IReadOnlyList<string> RemovalOnlyTeardowns(
    IEnumerable<(string Relative, string Text)> files
  ) =>
    [
      .. files
        .Where(f =>
          RemovedOverride.IsMatch(f.Text)
          && !UnloadedOverride.IsMatch(f.Text)
          && !f.Text.Contains(OptOut, StringComparison.Ordinal)
        )
        .Select(f => f.Relative),
    ];

  /// <summary>Each file of <paramref name="files"/> carrying the <c>removal-only teardown:</c>
  /// marker and overriding no block entity <c>OnBlockRemoved()</c>.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns>The offenders' relative paths, in input order.</returns>
  public static IReadOnlyList<string> StrandedOptOuts(
    IEnumerable<(string Relative, string Text)> files
  ) =>
    [
      .. files
        .Where(f =>
          f.Text.Contains(OptOut, StringComparison.Ordinal)
          && !RemovedOverride.IsMatch(f.Text)
        )
        .Select(f => f.Relative),
    ];

  /// <summary>One entry per <c>removal-only teardown:</c> marker line in
  /// <paramref name="files"/> with under ten characters of reason after it.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns>The relative path per such line, in input order.</returns>
  public static IReadOnlyList<string> UnexplainedOptOuts(
    IEnumerable<(string Relative, string Text)> files
  ) {
    var offenders = new List<string>();
    foreach (var (rel, text) in files)
      foreach (string line in Regex.Split(text, "\r\n|\r|\n")) {
        int at = line.IndexOf(OptOut, StringComparison.Ordinal);
        if (at < 0)
          continue;
        if (line[(at + OptOut.Length)..].Trim().Length < 10)
          offenders.Add(rel);
      }
    return offenders;
  }

  [Fact]
  public void A_block_entity_that_tears_down_on_removal_also_tears_down_on_unload() {
    IReadOnlyList<(string Relative, string Text)> files = Sources();
    IReadOnlyList<string> offenders = RemovalOnlyTeardowns(files);

    Assert.True(
      files.Any(f => RemovedOverride.IsMatch(f.Text)),
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
    IReadOnlyList<(string Relative, string Text)> files = Sources();
    IReadOnlyList<string> offenders = StrandedOptOuts(files);

    Assert.True(
      files.Any(f => f.Text.Contains(OptOut, StringComparison.Ordinal)),
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
    IReadOnlyList<string> offenders = UnexplainedOptOuts(Sources());

    Assert.True(
      offenders.Count == 0,
      "The opt-out marker must be followed by the reason on the same line:\n    "
        + string.Join("\n    ", offenders)
    );
  }

  private const string Removed = "public override void OnBlockRemoved() { }\n";
  private const string Marker =
    "// removal-only teardown: the server drops it with the chunk\n";

  // Fails when RemovalOnlyTeardowns passes a removal teardown with no unload and no marker, takes
  // the Block overload for the entity one, or names one with an unload or a marker.
  [Fact]
  [PlantedDefect(typeof(TeardownSymmetryTests), nameof(RemovalOnlyTeardowns))]
  public void A_removal_teardown_without_an_unload_is_named() {
    Assert.Equal(
      ["bare.cs"],
      RemovalOnlyTeardowns([
        ("bare.cs", Removed),
        ("both.cs", Removed + "public override void OnBlockUnloaded() { }"),
        ("marked.cs", Removed + Marker),
        (
          "block.cs",
          "public override void OnBlockRemoved(IWorldAccessor w, BlockPos p) { }"
        ),
      ])
    );
  }

  // Fails when StrandedOptOuts passes a marker on a file with no removal teardown or names one
  // beside it.
  [Fact]
  [PlantedDefect(typeof(TeardownSymmetryTests), nameof(StrandedOptOuts))]
  public void A_marker_away_from_a_removal_teardown_is_named() {
    Assert.Equal(
      ["stranded.cs"],
      StrandedOptOuts([
        ("stranded.cs", Marker),
        ("marked.cs", Removed + Marker),
      ])
    );
  }

  // Fails when UnexplainedOptOuts passes a marker with a short reason or names a full one.
  [Fact]
  [PlantedDefect(typeof(TeardownSymmetryTests), nameof(UnexplainedOptOuts))]
  public void A_marker_without_its_reason_is_named() {
    Assert.Equal(
      ["terse.cs"],
      UnexplainedOptOuts([
        ("marked.cs", Removed + Marker),
        ("terse.cs", Removed + "// removal-only teardown: none\r\n"),
      ])
    );
  }

  #endregion
}
