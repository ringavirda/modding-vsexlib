using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every <c>ExpandedLib*</c> package version under <c>templates/</c>, every
/// <c>"exlib": "&lt;version&gt;"</c> literal in <c>exdocs/exlib/wiki/Getting-Started.md</c>, and each sample's
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

  private static readonly Regex ExlibDependency = new(
    @"""exlib""\s*:\s*""([^""]+)"""
  );

  /// <summary>Each <c>ExpandedLib*</c> package reference in <paramref name="projects"/> whose
  /// <c>Version</c> is not <paramref name="version"/> or is missing.</summary>
  /// <param name="projects">Project files as their path and text.</param>
  /// <param name="version">The version every pin must name.</param>
  /// <returns><c>Matched</c>: the <c>ExpandedLib*</c> references seen. <c>Stale</c>:
  /// <c>path: Package=version</c> or <c>path: Package names no Version.</c>, in input order.
  /// </returns>
  public static (int Matched, IReadOnlyList<string> Stale) StalePackagePins(
    IEnumerable<(string Path, string Text)> projects,
    string version
  ) {
    int matched = 0;
    var stale = new List<string>();
    foreach (var (file, text) in projects)
      foreach (Match tag in PackageReferenceTag.Matches(text)) {
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
        if (!ver.Success)
          stale.Add($"{file}: {include.Groups[1].Value} names no Version.");
        else if (ver.Groups[1].Value != version)
          stale.Add($"{file}: {include.Groups[1].Value}={ver.Groups[1].Value}");
      }
    return (matched, stale);
  }

  /// <summary>Each <c>"exlib": "&lt;version&gt;"</c> literal in <paramref name="text"/> whose
  /// version is not <paramref name="version"/>.</summary>
  /// <returns>The stale versions, in text order.</returns>
  public static IReadOnlyList<string> StaleExlibDependencies(
    string text,
    string version
  ) =>
    [
      .. ExlibDependency
        .Matches(text)
        .Select(m => m.Groups[1].Value)
        .Where(found => found != version),
    ];

  [Fact]
  public void Every_ExpandedLib_package_version_under_templates_matches_modinfo() {
    string version = ModinfoVersion;
    string templatesDir = Path.Combine(RepoPaths.Root, "templates");

    var (matched, stale) = StalePackagePins(
      Directory
        .EnumerateFiles(templatesDir, "*.csproj", SearchOption.AllDirectories)
        .Select(f => (f, File.ReadAllText(f))),
      version
    );

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
    string page = Path.Combine(RepoPaths.Wiki, "Getting-Started.md");
    string text = File.ReadAllText(page);

    Assert.True(
      ExlibDependency.Matches(text).Count > 0,
      $"No \"exlib\" dependency literal found in {page}."
    );

    IReadOnlyList<string> stale = StaleExlibDependencies(text, version);

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
      Assert.True(
        ExlibDependency.Matches(text).Count > 0,
        $"{file} names no \"exlib\" dependency."
      );
      foreach (string found in StaleExlibDependencies(text, version))
        stale.Add($"{file}: exlib={found}");
    }

    Assert.True(
      stale.Count == 0,
      $"src/ExpandedLib/modinfo.json's version is {version}; stale sample exlib dependency"
        + $" floor(s):\n  {string.Join("\n  ", stale)}"
    );
  }

  // Fails when StalePackagePins passes a stale or missing version, counts another package, or
  // reads attributes in one order only.
  [Fact]
  [PlantedDefect(typeof(VersionPinTests), nameof(StalePackagePins))]
  public void A_stale_or_missing_package_pin_is_named() {
    var (matched, stale) = StalePackagePins(
      [
        (
          "a.csproj",
          "<PackageReference Version=\"0.8.1\" Include=\"ExpandedLib.Testing\" />\n"
            + "<PackageReference Include=\"ExpandedLib\" Version=\"0.8.2\" />\n"
            + "<PackageReference Include=\"xunit\" Version=\"1.0\" />\n"
            + "<PackageReference Include=\"ExpandedLib.Industry\" />"
        ),
      ],
      "0.8.2"
    );

    Assert.Equal(3, matched);
    Assert.Equal(
      [
        "a.csproj: ExpandedLib.Testing=0.8.1",
        "a.csproj: ExpandedLib.Industry names no Version.",
      ],
      stale
    );
  }

  // Fails when StaleExlibDependencies passes a stale version or names a current one or another
  // mod's.
  [Fact]
  [PlantedDefect(typeof(VersionPinTests), nameof(StaleExlibDependencies))]
  public void A_stale_exlib_dependency_is_named() {
    Assert.Equal(
      ["0.8.1"],
      StaleExlibDependencies(
        "{ \"exlib\": \"0.8.1\", \"exlibx\": \"0.1.0\", \"exlib\" : \"0.8.2\" }",
        "0.8.2"
      )
    );
  }
}
