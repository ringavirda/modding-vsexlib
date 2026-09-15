using System;
using System.Collections.Generic;
using System.IO;
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

  [Fact]
  public void A_block_entity_that_stores_a_stack_maps_its_collectibles() {
    var offenders = new List<string>();
    int files = 0;
    foreach (string f in SourceFiles()) {
      files++;
      string text = File.ReadAllText(f);
      if (!text.Contains("class BlockEntity") || !StoresAStack(text))
        continue;
      if (text.Contains("OnStoreCollectibleMappings"))
        continue;
      offenders.Add(Rel(f));
    }

    Assert.True(
      files > 0,
      "Found no C# sources under any mod's own source tree - the source walk is wrong, and "
        + "this rule would pass by scanning nothing."
    );
    Assert.True(offenders.Count == 0, string.Join(", ", offenders));
  }
}
