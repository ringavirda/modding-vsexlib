using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Runs the per-mod JSON-defect rule (<see cref="ShippedJson"/>) over exlib's own tree, plus
/// the whole-repo guards that compare one domain's assets against another's.</summary>
public class ShippedAssetJsonTests {
  // Exlib has no patches/ folder; only the JSON-parses half of ShippedJson.Check applies here.
  [Fact]
  public void Exlibs_own_tree_carries_no_shipped_json_defect() {
    IReadOnlyList<string> offenders = ShippedJson.Check(
      ExpandedLib.Testing.RepoPaths.Assets("exlib")
    );
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  [Fact]
  public void The_corpus_covers_every_shipped_domain() {
    var covered = AssetFilesByDomain()
      .Select(f => f.Domain)
      .Distinct()
      .ToHashSet(StringComparer.Ordinal);

    Assert.Equal(ShippedDomains().OrderBy(d => d), covered.OrderBy(d => d));
  }

  #region Corpus

  /// <summary>The asset domains the build packs: one folder under <c>mods/*/assets/</c> per shipped
  /// domain, including <c>game</c> for a mod's vanilla lang overlay.</summary>
  private static IEnumerable<string> ShippedDomains() {
    var domains = ExpandedLib
      .Testing.RepoPaths.AllAssetTrees()
      .Select(d => new DirectoryInfo(d).Name)
      .Distinct()
      .ToList();

    Assert.NotEmpty(domains);
    return domains;
  }

  /// <summary>Every JSON under a shipped domain, repo-relative and forward-slashed, paired with the
  /// domain it shipped under.</summary>
  private static IEnumerable<(
    string Relative,
    string Domain
  )> AssetFilesByDomain() {
    string root = RepoRoot();
    foreach (string dir in ExpandedLib.Testing.RepoPaths.AllAssetTrees()) {
      string domain = new DirectoryInfo(dir).Name;
      foreach (
        string file in Directory.EnumerateFiles(
          dir,
          "*.json",
          SearchOption.AllDirectories
        )
      )
        yield return (
          Path.GetRelativePath(root, file).Replace('\\', '/'),
          domain
        );
    }
  }

  private static IEnumerable<string> AssetFiles() =>
    AssetFilesByDomain().Select(f => f.Relative);

  /// <summary>Every texture one of our own domains names, anywhere in a shipped JSON asset, must have
  /// a file behind it. Shapes are resolved by <c>DefinitionAssets.MissingShapes</c>.</summary>
  [Fact]
  public void Every_texture_our_domains_name_resolves_to_a_file() {
    // vanilla's textures live in the game install, not in this repository.
    var domains = ShippedDomains().Where(d => d != "game").ToHashSet();
    var missing = new List<string>();

    foreach (string relative in AssetFiles()) {
      if (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        continue;
      foreach (
        Match m in TextureRef.Matches(
          File.ReadAllText(Path.Combine(RepoRoot(), relative))
        )
      ) {
        string domain = m.Groups["domain"].Value;
        if (!domains.Contains(domain))
          continue; // other mods' textures live outside this repo

        string file = Path.Combine(
          ExpandedLib.Testing.RepoPaths.Assets(domain),
          "textures",
          m.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar)
            + ".png"
        );
        if (!File.Exists(file))
          missing.Add($"{relative}: '{m.Value}'");
      }
    }

    Assert.True(
      missing.Count == 0,
      $"{missing.Count} texture reference(s) point where no file exists:\n  "
        + string.Join("\n  ", missing.Take(30))
        + (missing.Count > 30 ? $"\n  ... and {missing.Count - 30} more" : "")
    );
  }

  /// <summary>No shipped asset may carry an authoring-machine path: an editable shape names its
  /// textures by absolute path until export rewrites them to a <c>domain:path</c> asset code.</summary>
  [Fact]
  public void No_shipped_asset_carries_an_authoring_path() {
    // A drive letter (exactly one letter, then the colon, excludes a URL scheme), and the two
    // authoring-side trees.
    var authoring = new Regex(
      @"\b[A-Za-z]:[\\/]|\.game/|workbench/",
      RegexOptions.Compiled
    );
    var offenders = new List<string>();

    foreach (string relative in AssetFiles()) {
      string text = File.ReadAllText(Path.Combine(RepoRoot(), relative));
      if (authoring.Match(text) is { Success: true } hit)
        offenders.Add($"{relative}: contains '{hit.Value}'");
    }

    Assert.True(
      offenders.Count == 0,
      $"{offenders.Count} shipped asset(s) carry an authoring path - re-export them:\n  "
        + string.Join("\n  ", offenders.Take(30))
    );
  }

  // A domain-qualified texture path as a JSON string value: "iiex:block/metal/castiron".
  private static readonly Regex TextureRef = new(
    @"""(?<domain>[a-z]+):(?<path>block/[A-Za-z0-9_./-]+|item/[A-Za-z0-9_./-]+)""",
    RegexOptions.Compiled
  );

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
}
