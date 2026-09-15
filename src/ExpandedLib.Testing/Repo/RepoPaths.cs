using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Repo-relative path resolution for the per-mod layout (<c>mods/&lt;mod&gt;/{src,tests,assets,docs}</c>).
/// Every repo path a test or guard needs is resolved here.
/// </summary>
public static class RepoPaths {
  /// <summary>The solution root, resolved like <see cref="DefinitionGoldens.RepoRoot"/>: up to the
  /// nearest <c>.sln</c>/<c>.slnx</c>/<c>.git</c>, or an override outside a checkout.</summary>
  public static string Root => DefinitionGoldens.RepoRoot();

  // Maps each domain to its owning mod folder, built once from RepoManifest.
  private static readonly Dictionary<string, string> DomainToMod =
    BuildDomainToMod();

  private static Dictionary<string, string> BuildDomainToMod() {
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (string modId in RepoManifest.Mods.Keys)
      map[modId] = modId;
    foreach ((string domain, string owner) in RepoManifest.Overlays)
      map[domain] = owner;
    return map;
  }

  /// <summary>Registers a domain's owning mod folder; a second call for the same domain replaces
  /// the first.</summary>
  public static void Register(string domain, string modFolder) =>
    DomainToMod[domain] = modFolder;

  /// <summary>The absolute path to a mod's or a sample's folder root, resolved through
  /// <see cref="RepoManifest"/>: <see cref="RepoManifest.Mods"/>, then
  /// <see cref="RepoManifest.Samples"/>, else <c>mods/&lt;id&gt;</c>.</summary>
  public static string Mod(string id) {
    if (RepoManifest.Mods.TryGetValue(id, out string? modPath))
      return modPath;
    if (
      RepoManifest.Samples.TryGetValue(id, out RepoManifest.SampleEntry sample)
    )
      return sample.Path;
    return Path.Combine(Root, "mods", id);
  }

  /// <summary>The absolute path to a domain's packaged asset tree,
  /// <c>&lt;mod or sample path&gt;/assets/&lt;domain&gt;</c>. An unknown domain falls back to
  /// <c>mods/&lt;domain&gt;</c>, not an exception.</summary>
  public static string Assets(string domain) {
    string mod = DomainToMod.TryGetValue(domain, out string? registered)
      ? registered
      : domain;
    return Path.Combine(Mod(mod), "assets", domain);
  }

  /// <summary>The absolute path to <c>mods/&lt;modId&gt;/docs</c> (handbook, screenshots, moddb pages).</summary>
  public static string Docs(string modId) => Path.Combine(Mod(modId), "docs");

  /// <summary>The absolute path to <paramref name="id"/>'s source folder: <c>&lt;mod path&gt;/src</c> when
  /// that directory exists, else the mod path itself.</summary>
  public static string Src(string id) {
    string modPath = Mod(id);
    string srcPath = Path.Combine(modPath, "src");
    return Directory.Exists(srcPath) ? srcPath : modPath;
  }

  /// <summary>Every packaged asset-domain directory under any mod or sample:
  /// <c>mods/*/assets/*</c> and <c>samples/*/assets/*</c>.</summary>
  public static IReadOnlyList<string> AllAssetTrees() {
    IEnumerable<string> roots = RepoManifest.Mods.Values.Concat(
      RepoManifest.Samples.Values.Select(s => s.Path)
    );

    return roots
      .Select(root => Path.Combine(root, "assets"))
      .Where(Directory.Exists)
      .SelectMany(Directory.EnumerateDirectories)
      .OrderBy(p => p, StringComparer.Ordinal)
      .ToList();
  }
}
