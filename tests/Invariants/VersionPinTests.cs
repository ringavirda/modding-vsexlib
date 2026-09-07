using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A first-time modder's two entry paths - the test template and the wiki - each hand-pin a
/// version of their own instead of reading <c>src/modinfo.json</c>, so nothing stops either from
/// drifting stale the way both had (0.7.3 against a shipped 0.8.0-preview.1). This binds them: every
/// <c>ExpandedLib*</c> package version under <c>templates/</c> and every <c>"exlib": "&lt;version&gt;"</c>
/// dependency literal in <c>wiki/Getting-Started.md</c> must equal <see cref="ModinfoVersion"/>.
/// </summary>
public class VersionPinTests {
  private static string ModinfoVersion {
    get {
      string text = File.ReadAllText(
        Path.Combine(RepoPaths.Root, "src", "modinfo.json")
      );
      Match m = Regex.Match(text, @"""version""\s*:\s*""([^""]+)""");
      Assert.True(m.Success, "src/modinfo.json names no \"version\".");
      return m.Groups[1].Value;
    }
  }

  private static readonly Regex PackageVersion = new(
    @"<PackageReference\s+Include=""(ExpandedLib[^""]*)""\s+Version=""([^""]+)"""
  );

  [Fact]
  public void Every_ExpandedLib_package_version_under_templates_matches_modinfo() {
    string version = ModinfoVersion;
    string templatesDir = Path.Combine(RepoPaths.Root, "templates");

    var stale = new List<string>();
    foreach (
      string file in Directory.EnumerateFiles(
        templatesDir,
        "*.csproj",
        SearchOption.AllDirectories
      )
    ) {
      foreach (Match m in PackageVersion.Matches(File.ReadAllText(file))) {
        string found = m.Groups[2].Value;
        if (found != version)
          stale.Add($"{file}: {m.Groups[1].Value}={found}");
      }
    }

    Assert.True(
      stale.Count == 0,
      $"src/modinfo.json's version is {version}; stale pin(s):\n  "
        + string.Join("\n  ", stale)
    );
  }

  [Fact]
  public void Every_exlib_dependency_literal_in_Getting_Started_matches_modinfo() {
    string version = ModinfoVersion;
    string page = Path.Combine(RepoPaths.Root, "wiki", "Getting-Started.md");
    string text = File.ReadAllText(page);

    var stale = Regex
      .Matches(text, @"""exlib""\s*:\s*""([^""]+)""")
      .Select(m => m.Groups[1].Value)
      .Where(found => found != version)
      .ToList();

    Assert.True(
      stale.Count == 0,
      $"src/modinfo.json's version is {version}; {page} names stale exlib dependency"
        + $" version(s): {string.Join(", ", stale)}"
    );
  }
}
