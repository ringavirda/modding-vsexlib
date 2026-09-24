using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Repo-wide rule: a block entity storing an <c>ItemStack</c> must also override
/// <c>OnStoreCollectibleMappings</c> (and <c>OnLoadCollectibleMappings</c>), since
/// <c>ItemStack.ToBytes</c> serialises the collectible's runtime id, not its code.
/// </summary>
public class CollectibleMappingGuardTests {
  #region Corpus

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

  // Matches SetItemstack, MoltenContents.Write, or MoltenCharge.ToTree (narrowed to files naming MoltenCharge).
  private static bool StoresAStack(string text) =>
    text.Contains("SetItemstack(")
    || text.Contains("MoltenContents.Write(")
    || (text.Contains("MoltenCharge") && text.Contains(".ToTree("));

  #endregion

  /// <summary>Each file of <paramref name="files"/> declaring a block entity that stores a stack
  /// (<c>SetItemstack</c>, <c>MoltenContents.Write</c>, <c>MoltenCharge</c>'s <c>ToTree</c>) and
  /// never names <c>OnStoreCollectibleMappings</c>.</summary>
  /// <param name="files">Sources as their relative path and text.</param>
  /// <returns>The offenders' relative paths, in input order.</returns>
  public static IReadOnlyList<string> UnmappedStacks(
    IEnumerable<(string Relative, string Text)> files
  ) =>
    [
      .. files
        .Where(f =>
          f.Text.Contains("class BlockEntity")
          && StoresAStack(f.Text)
          && !f.Text.Contains("OnStoreCollectibleMappings")
        )
        .Select(f => f.Relative),
    ];

  [Fact]
  public void A_block_entity_that_stores_a_stack_maps_its_collectibles() {
    List<(string Relative, string Text)> files =
    [
      .. Premise
        .NotEmpty(SourceFiles(), "mod source files")
        .Select(f => (Rel(f), File.ReadAllText(f))),
    ];
    List<string> offenders = [.. UnmappedStacks(files)];

    Assert.True(
      files.Count > 0,
      "Found no C# sources under any mod's own source tree - the source walk is wrong, and "
        + "this rule would pass by scanning nothing."
    );
    Assert.True(offenders.Count == 0, string.Join(", ", offenders));
  }

  // Fails when UnmappedStacks passes a block entity storing a stack by any of the three writes
  // without the mapping, or names one that maps, a block, or a MoltenCharge read.
  [Fact]
  [PlantedDefect(typeof(CollectibleMappingGuardTests), nameof(UnmappedStacks))]
  public void A_block_entity_storing_a_stack_without_mappings_is_named() {
    const string be = "class BlockEntityKiln : BlockEntity {\n";
    Assert.Equal(
      ["slot.cs", "molten.cs", "charge.cs"],
      UnmappedStacks([
        ("slot.cs", be + "  void S() => t.SetItemstack(\"k\", s);\n}"),
        ("molten.cs", be + "  void S() => MoltenContents.Write(t, c);\n}"),
        ("charge.cs", be + "  MoltenCharge c;\n  void S() => c.ToTree(t);\n}"),
        (
          "mapped.cs",
          be
            + "  void S() => t.SetItemstack(\"k\", s);\n"
            + "  public override void OnStoreCollectibleMappings() { }\n}"
        ),
        (
          "block.cs",
          "class Kiln : Block {\n  void S() => t.SetItemstack(\"k\", s);\n}"
        ),
        ("read.cs", be + "  MoltenCharge c;\n}"),
      ])
    );
  }
}
