using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every <c>ExpandedLib*</c> package version under <c>templates/</c>, every
/// <c>"exlib": "&lt;version&gt;"</c> literal in <c>wiki/Getting-Started.md</c>, and each sample's
/// <c>exlib</c> dependency floor must equal <see cref="ModinfoVersion"/>.</summary>
public class VersionPinTests {
  private static string ModinfoVersion {
    get {
      string text = File.ReadAllText(
        Path.Combine(RepoPaths.Root, "src", "ExpandedLib", "modinfo.json")
      );
      Match m = Regex.Match(text, @"""version""\s*:\s*""([^""]+)""");
      Assert.True(
        m.Success,
        "src/ExpandedLib/modinfo.json names no \"version\"."
      );
      return m.Groups[1].Value;
    }
  }

  // The element first, then its attributes, matched independently of order.
  private static readonly Regex PackageReferenceTag = new(
    @"<PackageReference\b([^>]*)>"
  );
  private static readonly Regex IncludeAttr = new(
    @"Include\s*=\s*""([^""]+)"""
  );
  private static readonly Regex VersionAttr = new(
    @"Version\s*=\s*""([^""]+)"""
  );

  [Fact]
  public void Every_ExpandedLib_package_version_under_templates_matches_modinfo() {
    string version = ModinfoVersion;
    string templatesDir = Path.Combine(RepoPaths.Root, "templates");

    int matched = 0;
    var stale = new List<string>();
    foreach (
      string file in Directory.EnumerateFiles(
        templatesDir,
        "*.csproj",
        SearchOption.AllDirectories
      )
    ) {
      foreach (Match tag in PackageReferenceTag.Matches(File.ReadAllText(file))) {
        string attrs = tag.Groups[1].Value;
        Match include = IncludeAttr.Match(attrs);
        if (
          !include.Success
          || !include
            .Groups[1]
            .Value.StartsWith("ExpandedLib", StringComparison.Ordinal)
        )
          continue;
        matched++;

        Match ver = VersionAttr.Match(attrs);
        Assert.True(
          ver.Success,
          $"{file}: {include.Groups[1].Value} names no Version."
        );
        string found = ver.Groups[1].Value;
        if (found != version)
          stale.Add($"{file}: {include.Groups[1].Value}={found}");
      }
    }

    Assert.True(
      matched > 0,
      $"No ExpandedLib* PackageReference found under {templatesDir}."
    );
    Assert.True(
      stale.Count == 0,
      $"src/ExpandedLib/modinfo.json's version is {version}; stale pin(s):\n  "
        + string.Join("\n  ", stale)
    );
  }

  [Fact]
  public void Every_exlib_dependency_literal_in_Getting_Started_matches_modinfo() {
    string version = ModinfoVersion;
    string page = Path.Combine(RepoPaths.Root, "wiki", "Getting-Started.md");
    string text = File.ReadAllText(page);

    MatchCollection literals = Regex.Matches(
      text,
      @"""exlib""\s*:\s*""([^""]+)"""
    );
    Assert.True(
      literals.Count > 0,
      $"No \"exlib\" dependency literal found in {page}."
    );

    var stale = literals
      .Select(m => m.Groups[1].Value)
      .Where(found => found != version)
      .ToList();

    Assert.True(
      stale.Count == 0,
      $"src/ExpandedLib/modinfo.json's version is {version}; {page} names stale exlib dependency"
        + $" version(s): {string.Join(", ", stale)}"
    );
  }

  [Fact]
  public void Every_sample_exlib_dependency_floor_matches_modinfo() {
    string version = ModinfoVersion;

    // Driven from exmod.json's own sample map, not a directory walk.
    var stale = new List<string>();
    foreach (
      (string name, RepoManifest.SampleEntry sample) in RepoManifest.Samples
    ) {
      string file = Path.Combine(sample.Path, "src", "modinfo.json");
      Assert.True(File.Exists(file), $"Sample '{name}' has no {file}.");

      string text = File.ReadAllText(file);
      MatchCollection literals = Regex.Matches(
        text,
        @"""exlib""\s*:\s*""([^""]+)"""
      );
      Assert.True(literals.Count > 0, $"{file} names no \"exlib\" dependency.");
      foreach (Match m in literals) {
        if (m.Groups[1].Value != version)
          stale.Add($"{file}: exlib={m.Groups[1].Value}");
      }
    }

    Assert.True(
      stale.Count == 0,
      $"src/ExpandedLib/modinfo.json's version is {version}; stale sample exlib dependency"
        + $" floor(s):\n  {string.Join("\n  ", stale)}"
    );
  }
}
