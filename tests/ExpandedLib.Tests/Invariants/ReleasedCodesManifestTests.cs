using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>exlib's release seed (<see cref="ReleasedHistorySeed"/>) reaches the newest release
/// tag: a release that goes out without its row leaves the migration contract a release behind.
/// </summary>
public class ReleasedCodesManifestTests {
  private static readonly Regex ReleaseTag = new(@"^v(\d+\.\d+\.\d+)$");

  /// <summary>How <paramref name="seeded"/> falls short of the newest release among
  /// <paramref name="tags"/>, or null when it is that release.</summary>
  /// <param name="seeded">The newest version the seed registers, or null when it registers none.
  /// </param>
  /// <param name="tags">Git tag names; only <c>v&lt;major.minor.patch&gt;</c> without a
  /// pre-release suffix counts as a release.</param>
  public static string? NewestReleaseMismatch(
    string? seeded,
    IEnumerable<string> tags
  ) {
    string? newest = tags.Select(t => ReleaseTag.Match(t))
      .Where(m => m.Success)
      .Select(m => m.Groups[1].Value)
      .OrderBy(v => v, Comparer<string>.Create(ReleasedVersions.Compare))
      .LastOrDefault();
    if (newest == null)
      return "no release tag v<major.minor.patch> among the tags";
    if (seeded == newest)
      return null;
    return $"the newest release tag is v{newest} but the seed's newest release is "
      + $"{seeded ?? "(none)"}; add a ReleasedHistory row per release in between, read from its tag";
  }

  private static string[] Tags() {
    var git = new ProcessStartInfo("git", "tag --list v*") {
      WorkingDirectory = RepoPaths.Root,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };
    using Process process = Process.Start(git)!;
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
  }

  // Fails when a release is tagged and the seed gains no row for it.
  [Fact]
  public void The_seed_reaches_the_newest_release_tag() {
    IReadOnlyList<string> tags = Premise.NotEmpty(Tags(), "git tags v*");
    ReleasedVersions.HighestPublished.TryGetValue("exlib", out string? seeded);

    string? mismatch = NewestReleaseMismatch(seeded, tags);

    Assert.True(mismatch == null, mismatch);
  }

  // Fails when NewestReleaseMismatch takes a pre-release or the first tag listed for the newest,
  // orders versions as text, or passes a seed behind the newest release.
  [Fact]
  [PlantedDefect(
    typeof(ReleasedCodesManifestTests),
    nameof(NewestReleaseMismatch)
  )]
  public void A_seed_behind_the_newest_release_is_named() {
    string[] tags = ["v0.9.0", "v0.10.0", "v0.10.1-preview.1", "v0.9.1"];

    string? behind = NewestReleaseMismatch("0.9.1", tags);

    Assert.NotNull(behind);
    Assert.Contains("v0.10.0", behind);
    Assert.Contains("0.9.1", behind);
    Assert.Null(NewestReleaseMismatch("0.10.0", tags));
    Assert.NotNull(NewestReleaseMismatch(null, tags));
    Assert.NotNull(NewestReleaseMismatch("0.10.0", ["v0.10.0-preview.1"]));
  }
}
