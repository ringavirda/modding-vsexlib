using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>One mod's contribution to the release history: what it has ever shipped, its highest
/// published version, and the codes from that shipping it has not yet migrated.</summary>
public sealed record ReleasedModHistory(
  IReadOnlyList<ReleasedCodes.Shipped> Shipped,
  IReadOnlyList<ReleasedCodes.ShippedEntityClass> EntityClasses,
  IReadOnlyDictionary<string, string> Versions,
  IReadOnlyList<string> Debt
);

/// <summary>
/// Registry of released-code history, one entry per mod. Each mod's own test <c>ModuleInit</c>
/// registers its shipped codes, entity classes, published versions and migration debt once, and
/// then one row per later release; <see cref="ReleasedCodes"/>, <see cref="ReleasedVersions"/> and
/// <see cref="ReleasedCodeDebt"/> read it back through their old entry points.
/// </summary>
public static class ReleasedHistory {
  /// <summary>One published release of a mod: its version and the blocktypes it shipped for the
  /// first time.</summary>
  /// <param name="Version">The published version, <c>major.minor.patch</c> with an optional
  /// <c>-suffix</c>.</param>
  /// <param name="Added">The blocktypes the release shipped for the first time; empty when it added
  /// no code.</param>
  public sealed record Release(
    string Version,
    IReadOnlyList<ReleasedCodes.Shipped> Added
  );

  private static readonly Dictionary<string, ReleasedModHistory> ByMod = new();
  private static readonly Dictionary<string, List<Release>> ReleasesByMod =
    new();

  /// <summary>Registers <paramref name="mod"/>'s shipped history. Call once, from that mod's test
  /// <c>ModuleInit</c>; a second call for the same mod replaces the first and keeps the release
  /// rows registered through the other overload.</summary>
  public static void Register(
    string mod,
    IReadOnlyList<ReleasedCodes.Shipped> shipped,
    IReadOnlyList<ReleasedCodes.ShippedEntityClass> entityClasses,
    IReadOnlyDictionary<string, string> versions,
    IReadOnlyList<string> debt
  ) =>
    ByMod[mod] = new ReleasedModHistory(shipped, entityClasses, versions, debt);

  /// <summary>Registers one release of <paramref name="mod"/>, published under the modid
  /// <paramref name="mod"/>: its codes join the mod's shipped history and its version counts
  /// toward <see cref="AllVersions"/>. A second row for the same version replaces the first.
  /// </summary>
  /// <param name="mod">The mod, which is also the modid the release was published under.</param>
  /// <param name="version">The published version, <c>major.minor.patch</c> with an optional
  /// <c>-suffix</c>.</param>
  /// <param name="added">The blocktypes the release shipped for the first time; empty when it
  /// added none.</param>
  /// <exception cref="ArgumentException"><paramref name="version"/> does not start with
  /// <c>major.minor.patch</c>.</exception>
  public static void Register(
    string mod,
    string version,
    IReadOnlyList<ReleasedCodes.Shipped> added
  ) {
    _ = ReleasedVersions.Compare(version, version);
    if (!ReleasesByMod.TryGetValue(mod, out List<Release>? rows))
      ReleasesByMod[mod] = rows = [];
    rows.RemoveAll(r => r.Version == version);
    rows.Add(new Release(version, added));
    rows.Sort((a, b) => ReleasedVersions.Compare(a.Version, b.Version));
  }

  /// <summary><paramref name="mod"/>'s release rows, oldest first.</summary>
  /// <param name="mod">The mod the rows were registered under.</param>
  /// <returns>A copy of the rows, ordered by <see cref="ReleasedVersions.Compare"/>; empty when none
  /// is registered.</returns>
  public static IReadOnlyList<Release> Releases(string mod) =>
    ReleasesByMod.TryGetValue(mod, out List<Release>? rows) ? [.. rows] : [];

  /// <summary><paramref name="mod"/>'s registered history, its release rows' codes and versions
  /// folded in.</summary>
  /// <param name="mod">The mod the history and rows were registered under.</param>
  /// <returns>The full registration with every row's codes appended to its shipped codes, and the
  /// newest row's version as the mod's own unless the registered one is as new or newer; the
  /// registration unchanged when there are no rows; null when neither was registered.</returns>
  /// <exception cref="ArgumentException">There are rows, and the full registration's version for
  /// <paramref name="mod"/> is not <c>major.minor.patch</c> with an optional
  /// <c>-suffix</c>.</exception>
  public static ReleasedModHistory? For(string mod) {
    ByMod.TryGetValue(mod, out ReleasedModHistory? history);
    IReadOnlyList<Release> rows = Releases(mod);
    if (rows.Count == 0)
      return history;
    history ??= new ReleasedModHistory(
      [],
      [],
      new Dictionary<string, string>(),
      []
    );
    var versions = new Dictionary<string, string>(history.Versions);
    string newest = rows[^1].Version;
    if (
      !versions.TryGetValue(mod, out string? known)
      || ReleasedVersions.Compare(newest, known) > 0
    )
      versions[mod] = newest;
    return history with {
      Shipped = [.. history.Shipped, .. rows.SelectMany(r => r.Added)],
      Versions = versions,
    };
  }

  /// <summary>Drops every registration and release row of <paramref name="mod"/>; does nothing
  /// when there are none.</summary>
  internal static void Forget(string mod) {
    ByMod.Remove(mod);
    ReleasesByMod.Remove(mod);
  }

  private static IEnumerable<ReleasedModHistory> All =>
    ByMod.Keys.Union(ReleasesByMod.Keys).Select(m => For(m)!);

  /// <summary>Every shipped blocktype across every registered mod.</summary>
  public static IEnumerable<ReleasedCodes.Shipped> AllShipped =>
    All.SelectMany(h => h.Shipped);

  /// <summary>Every shipped block-entity class across every registered mod.</summary>
  public static IEnumerable<ReleasedCodes.ShippedEntityClass> AllEntityClasses =>
    All.SelectMany(h => h.EntityClasses);

  /// <summary>The highest published version per modid, across every registered mod and release
  /// row.</summary>
  public static IReadOnlyDictionary<string, string> AllVersions {
    get {
      var highest = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach ((string id, string version) in All.SelectMany(h => h.Versions))
        if (
          !highest.TryGetValue(id, out string? seen)
          || ReleasedVersions.Compare(version, seen) > 0
        )
          highest[id] = version;
      return highest;
    }
  }

  /// <summary>Every recorded migration-debt code across every registered mod.</summary>
  public static IEnumerable<string> AllDebt => All.SelectMany(h => h.Debt);
}
