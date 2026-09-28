using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Registries;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// Golden-file oracle for code-first definition parity: every def reproduces its golden
/// (<see cref="CheckGolden"/>), and the golden set exactly covers the defs
/// (<see cref="CheckCompleteness"/>).
/// </summary>
/// <remarks>On a game series older than the current one, a def whose content differs on that
/// series, or that only that series has, keeps its golden under <see cref="SeriesRoot"/>, which
/// overrides the shared one; a shared golden whose def that series lacks is excused there by a
/// marker, the golden's path under <see cref="SeriesRoot"/> with <see cref="AbsentSuffix"/>
/// appended.</remarks>
public static class DefinitionGoldens {
  /// <summary>The game series this build runs against when it is older than the current one
  /// (<c>1.21</c>, <c>1.20</c>), or <c>null</c> on the current series.</summary>
  [CheckHelper("names the series whose own goldens the checks read")]
  public static readonly string? OlderSeries =
#if GAME_GE_1_22
    null;
#elif GAME_GE_1_21
    "1.21";
#else
    "1.20";
#endif

  /// <summary>The folder holding <paramref name="series"/>' own goldens: the sibling of
  /// <paramref name="goldenRoot"/> named <c>&lt;goldenRoot&gt;-&lt;series&gt;</c>, laid out as
  /// <paramref name="goldenRoot"/> is.</summary>
  [CheckHelper("names the folder of a series' own goldens")]
  public static string SeriesRoot(string goldenRoot, string series) =>
    goldenRoot.TrimEnd('/', '\\') + "-" + series;

  /// <summary>What a marker's name adds to the path of the shared golden it excuses:
  /// <c>goldens-1.20/iiex/blocktypes/x.json.absent</c> says the 1.20 series has no def for
  /// <c>goldens/iiex/blocktypes/x.json</c>.</summary>
  [CheckHelper("names the marker excusing a shared golden on a series")]
  public const string AbsentSuffix = ".absent";

  /// <summary>Every code-first def (blocks, items, recipes) <paramref name="asm"/> declares for
  /// <paramref name="domain"/>, without registering into the process-wide registry.</summary>
  [CheckHelper("collects a domain's code-first definitions")]
  public static IReadOnlyList<IExDef> Collect(string domain, Assembly asm) {
    var defs = new List<IExDef>();
    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      defs.AddRange(ExDefinitions.DefinitionsOf(type, domain));
      defs.AddRange(ExDefinitions.ItemDefinitionsOf(type, domain));
      defs.AddRange(ExDefinitions.RecipeDefinitionsOf(type, domain));
    }
    // Generated metal families have no provider class; emitted from config/metals JSON at runtime.
    defs.AddRange(EmittedFamilies(domain));
    return defs;
  }

  // Metal-family item defs the emitter produces, read from config/metals across every asset tree.
  private static IEnumerable<ExItemDef> EmittedFamilies(string domain) {
    var metals = new List<MetalDef>();
    foreach (string assetsRoot in RepoPaths.AllAssetTrees()) {
      foreach (
        string file in Directory.EnumerateFiles(
          assetsRoot,
          "*.json",
          SearchOption.AllDirectories
        )
      ) {
        if (!file.Replace('\\', '/').Contains("/config/metals/"))
          continue;
        MetalDef? metal = JsonConvert.DeserializeObject<MetalDef>(
          File.ReadAllText(file)
        );
        if (metal != null)
          metals.Add(metal);
      }
    }
    return MetalFamilyEmitter.Emit(metals).Where(d => d.Domain == domain);
  }

  /// <summary>The def's golden path relative to the golden root: <c>{domain}/{Location.Path}</c> (a stable,
  /// serializable key that doubles as the xUnit theory case id).</summary>
  [CheckHelper("names a definition's golden file")]
  public static string RelativePath(IExDef def) =>
    def.Location.Domain + "/" + def.Location.Path;

  /// <summary>One xUnit theory case per def, keyed by its <see cref="RelativePath"/>.</summary>
  [CheckHelper("feeds a theory the golden paths")]
  public static IEnumerable<object[]> Cases(string domain, Assembly asm) =>
    Collect(domain, asm)
      .Select(d => RelativePath(d))
      .OrderBy(p => p)
      .Select(p => new object[] { p });

  /// <summary>Checks the def whose <see cref="RelativePath"/> is <paramref name="relativePath"/>
  /// against its committed golden under <paramref name="goldenRoot"/>, or under
  /// <see cref="SeriesRoot"/> for <see cref="OlderSeries"/> where that holds one.</summary>
  /// <returns><c>(true, "")</c> on match, else a readable diff message.</returns>
  public static (bool ok, string message) CheckGolden(
    string domain,
    Assembly asm,
    string relativePath,
    string goldenRoot
  ) => CheckGolden(domain, asm, relativePath, goldenRoot, OlderSeries);

  internal static (bool ok, string message) CheckGolden(
    string domain,
    Assembly asm,
    string relativePath,
    string goldenRoot,
    string? series
  ) {
    IExDef def = Collect(domain, asm)
      .Single(d => RelativePath(d) == relativePath);
    string file = FullPath(goldenRoot, def);
    if (
      series != null
      && File.Exists(FullPath(SeriesRoot(goldenRoot, series), def))
    )
      file = FullPath(SeriesRoot(goldenRoot, series), def);
    if (!File.Exists(file))
      return (false, $"missing golden file: {file}");

    JToken expected = JToken.Parse(File.ReadAllText(file));
    return DefinitionParity.Equal(expected, def.ToJson(), out string normalized)
      ? (true, "")
      : (
        false,
        $"{relativePath} code-first def diverged from its golden:\n{normalized}"
      );
  }

  /// <summary>Completeness of the golden set: <c>missing</c> is defs with no golden file under
  /// <paramref name="goldenRoot"/> nor, for <see cref="OlderSeries"/>, under
  /// <see cref="SeriesRoot"/>; <c>orphans</c> is golden files no def claims, there and under
  /// <see cref="SeriesRoot"/>, bar a shared one a marker (<see cref="AbsentSuffix"/>) excuses on
  /// that series, and every stale marker: one whose def that series has, or whose shared golden
  /// is gone.</summary>
  /// <returns>Each list sorted; an orphan under <paramref name="goldenRoot"/> is named relative to
  /// it, one under <see cref="SeriesRoot"/> relative to that folder's parent, so it starts with
  /// the folder's name.</returns>
  public static (
    IReadOnlyList<string> missing,
    IReadOnlyList<string> orphans
  ) CheckCompleteness(string domain, Assembly asm, string goldenRoot) =>
    CheckCompleteness(domain, asm, goldenRoot, OlderSeries);

  internal static (
    IReadOnlyList<string> missing,
    IReadOnlyList<string> orphans
  ) CheckCompleteness(
    string domain,
    Assembly asm,
    string goldenRoot,
    string? series
  ) {
    var defs = Collect(domain, asm);
    string? seriesRoot = series == null ? null : SeriesRoot(goldenRoot, series);

    var missing = defs.Where(d =>
        !File.Exists(FullPath(goldenRoot, d))
        && (seriesRoot == null || !File.Exists(FullPath(seriesRoot, d)))
      )
      .Select(RelativePath)
      .OrderBy(p => p)
      .ToList();

    var orphans = Orphans(defs, domain, goldenRoot, goldenRoot)
      .Where(p => seriesRoot == null || !File.Exists(Marker(seriesRoot, p)))
      .ToList();
    if (seriesRoot != null) {
      string namedFrom = Path.GetDirectoryName(Path.GetFullPath(seriesRoot))!;
      orphans.AddRange(Orphans(defs, domain, seriesRoot, namedFrom));
      orphans.AddRange(
        StaleMarkers(defs, domain, goldenRoot, seriesRoot)
          .Select(f => Path.GetRelativePath(namedFrom, f).Replace('\\', '/'))
      );
      orphans.Sort(StringComparer.Ordinal);
    }

    return (missing, orphans);
  }

  // The marker under seriesRoot excusing the shared golden named relative to the golden root.
  private static string Marker(string seriesRoot, string relative) =>
    Path.Combine(seriesRoot, relative) + AbsentSuffix;

  // Markers under seriesRoot's domain folder whose shared golden a def claims or is gone.
  private static IEnumerable<string> StaleMarkers(
    IReadOnlyList<IExDef> defs,
    string domain,
    string goldenRoot,
    string seriesRoot
  ) {
    var claimed = defs.Select(d => Path.GetFullPath(FullPath(goldenRoot, d)))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    string domainRoot = Path.Combine(seriesRoot, domain);
    if (!Directory.Exists(domainRoot))
      return [];
    return Directory
      .EnumerateFiles(
        domainRoot,
        "*" + AbsentSuffix,
        SearchOption.AllDirectories
      )
      .Where(marker => {
        string shared = Path.GetFullPath(
          Path.Combine(
            goldenRoot,
            Path.GetRelativePath(seriesRoot, marker)[..^AbsentSuffix.Length]
          )
        );
        return claimed.Contains(shared) || !File.Exists(shared);
      })
      .OrderBy(f => f, StringComparer.Ordinal);
  }

  // Golden files under root's domain folder that no def claims, named relative to namedFrom.
  private static IEnumerable<string> Orphans(
    IReadOnlyList<IExDef> defs,
    string domain,
    string root,
    string namedFrom
  ) {
    var claimed = defs.Select(d => Path.GetFullPath(FullPath(root, d)))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    string domainRoot = Path.Combine(root, domain);
    return (
      Directory.Exists(domainRoot)
        ? Directory.EnumerateFiles(
          domainRoot,
          "*.json",
          SearchOption.AllDirectories
        )
        : []
    )
      .Where(f => !claimed.Contains(Path.GetFullPath(f)))
      .Select(f => Path.GetRelativePath(namedFrom, f).Replace('\\', '/'))
      .OrderBy(p => p);
  }

  /// <summary>Re-blesses the goldens under <paramref name="goldenRoot"/> from the current def
  /// output: every golden when <c>EXLIB_WRITE_GOLDENS</c> is <c>1</c>, else those whose
  /// <see cref="RelativePath"/> contains one of its comma-separated fragments. Opt-in: call only
  /// when <see cref="WriteRequested"/>.</summary>
  /// <remarks>On <see cref="OlderSeries"/> the shared goldens are left as they are: a selected def
  /// whose output differs from its shared golden, or has none, is written under
  /// <see cref="SeriesRoot"/>, and a series golden of a def that no longer differs is deleted. A
  /// selected shared golden whose def the series lacks gets its marker
  /// (<see cref="AbsentSuffix"/>), and a selected stale marker is deleted. A series write reads the
  /// shared goldens, so it runs after the current series' write, never beside it.
  /// A fragment whose first <c>/</c>-separated segment is another domain and whose second is a
  /// game asset category (a key of <see cref="AssetCategory.categories"/>, such as
  /// <c>blocktypes</c>, <c>itemtypes</c> or <c>recipes</c>) belongs to another assembly's goldens,
  /// is skipped here and never throws. A fragment naming a domain no suite has, with such a
  /// category, is skipped the same way.</remarks>
  /// <exception cref="InvalidOperationException">Any other fragment matches none of
  /// <paramref name="domain"/>'s goldens (on <see cref="OlderSeries"/>, nor a shared golden the
  /// series lacks); the message names the value and the fragment and says a fragment starts with
  /// its domain, and nothing is written.</exception>
  [CheckHelper("writes the goldens when asked")]
  public static void WriteAll(string domain, Assembly asm, string goldenRoot) =>
    WriteAll(
      domain,
      asm,
      goldenRoot,
      Environment.GetEnvironmentVariable("EXLIB_WRITE_GOLDENS") ?? ""
    );

  internal static void WriteAll(
    string domain,
    Assembly asm,
    string goldenRoot,
    string value
  ) => WriteAll(domain, asm, goldenRoot, value, OlderSeries);

  internal static void WriteAll(
    string domain,
    Assembly asm,
    string goldenRoot,
    string value,
    string? series
  ) {
    IReadOnlyList<string> only = WriteFilter(value);
    IReadOnlyList<IExDef> defs = Collect(domain, asm);
    string[] absent =
      series == null ? [] : [.. Orphans(defs, domain, goldenRoot, goldenRoot)];

    string[] unmatched =
    [
      .. only.Where(f =>
        !IsOtherSuites(f, domain)
        && !defs.Any(d => RelativePath(d).Contains(f, StringComparison.Ordinal))
        && !absent.Any(p => p.Contains(f, StringComparison.Ordinal))
      ),
    ];
    if (unmatched.Length > 0)
      throw new InvalidOperationException(
        $"EXLIB_WRITE_GOLDENS={value} names no {domain} golden: "
          + string.Join(", ", unmatched)
          + $"; a fragment starts with its domain, as in {domain}/"
      );

    foreach (IExDef def in defs) {
      string relative = RelativePath(def);
      if (
        only.Count > 0
        && !only.Any(f => relative.Contains(f, StringComparison.Ordinal))
      )
        continue;

      string file = FullPath(goldenRoot, def);
      if (series != null) {
        string shared = file;
        file = FullPath(SeriesRoot(goldenRoot, series), def);
        if (
          File.Exists(shared)
          && DefinitionParity.Equal(
            JToken.Parse(File.ReadAllText(shared)),
            def.ToJson()
          )
        ) {
          if (File.Exists(file))
            File.Delete(file);
          continue;
        }
      }
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);
      File.WriteAllText(file, def.ToJson().ToString());
    }
    if (series != null)
      Mark(
        defs,
        domain,
        goldenRoot,
        SeriesRoot(goldenRoot, series),
        absent,
        only
      );
  }

  // Writes a marker for each selected shared golden the series lacks and deletes each selected
  // stale marker.
  private static void Mark(
    IReadOnlyList<IExDef> defs,
    string domain,
    string goldenRoot,
    string seriesRoot,
    string[] absent,
    IReadOnlyList<string> only
  ) {
    bool Selected(string relative) =>
      only.Count == 0
      || only.Any(f => relative.Contains(f, StringComparison.Ordinal));
    foreach (string relative in absent.Where(Selected)) {
      string marker = Marker(seriesRoot, relative);
      Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
      File.WriteAllText(marker, "");
    }
    foreach (
      string marker in StaleMarkers(defs, domain, goldenRoot, seriesRoot)
        .Where(m =>
          Selected(
            Path.GetRelativePath(seriesRoot, m)[..^AbsentSuffix.Length]
              .Replace('\\', '/')
          )
        )
        .ToList()
    )
      File.Delete(marker);
  }

  private static bool IsOtherSuites(string fragment, string domain) {
    string[] segments = fragment.Split('/');
    return segments.Length > 1
      && segments[0].Length > 0
      && segments[0] != domain
      && AssetCategory.categories.ContainsKey(segments[1]);
  }

  /// <summary>True when <c>EXLIB_WRITE_GOLDENS</c> is set to anything non-empty.</summary>
  [CheckHelper("reads the golden-write switch")]
  public static bool WriteRequested =>
    !string.IsNullOrWhiteSpace(
      Environment.GetEnvironmentVariable("EXLIB_WRITE_GOLDENS")
    );

  /// <summary>The path fragments <paramref name="value"/> names, or empty for "every golden" (<c>1</c>).</summary>
  private static IReadOnlyList<string> WriteFilter(string value) {
    if (value.Trim() is "" or "1")
      return [];
    return
    [
      .. value
        .Split(
          ',',
          StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        )
        .Select(p => p.Replace('\\', '/').Replace(".json", "")),
    ];
  }

  /// <summary>The repo root every source-tree path is resolved against; also settable with the
  /// <c>EXLIB_REPO_ROOT</c> environment variable.</summary>
  [CheckHelper("overrides the repository root a run reads")]
  public static string? RepoRootOverride { get; set; }

  /// <summary>Resolves a repo-root-relative path to an absolute path, walking up from the test
  /// binary to the solution root.</summary>
  [CheckHelper("resolves a path under the repository root")]
  public static string SolutionRelative(string repoRelativePath) =>
    Path.Combine(
      RepoRoot(),
      repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
    );

  private static string FullPath(string goldenRoot, IExDef def) =>
    Path.Combine(
      goldenRoot,
      def.Location.Domain,
      def.Location.Path.Replace('/', Path.DirectorySeparatorChar)
    );

  // Ships as a consumable dev library; cannot assume this repo's own solution file name.
  // Any .sln, .slnx or .git marks a repo root.
  private static readonly string[] RootMarkers = ["*.sln", "*.slnx", ".git"];

  /// <summary>The resolved repo root: <see cref="RepoRootOverride"/>, else
  /// <c>EXLIB_REPO_ROOT</c>, else the first directory above the test binary carrying a
  /// <c>.sln</c>, <c>.slnx</c> or <c>.git</c>.</summary>
  [CheckHelper("finds the repository root")]
  public static string RepoRoot() {
    string? configured =
      RepoRootOverride ?? Environment.GetEnvironmentVariable("EXLIB_REPO_ROOT");
    if (!string.IsNullOrWhiteSpace(configured))
      return configured;

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !IsRepoRoot(dir))
      dir = dir.Parent;

    return dir?.FullName
      ?? throw new InvalidOperationException(
        "Could not locate the repo root from "
          + AppContext.BaseDirectory
          + ". Looked upward for a .sln, .slnx or .git. Set "
          + $"{nameof(DefinitionGoldens)}.{nameof(RepoRootOverride)} or the EXLIB_REPO_ROOT "
          + "environment variable when the harness runs outside a repository checkout."
      );
  }

  private static bool IsRepoRoot(DirectoryInfo dir) =>
    RootMarkers.Any(m =>
      m.StartsWith('*')
        ? dir.EnumerateFiles(m).Any()
        : Directory.Exists(Path.Combine(dir.FullName, m))
          || File.Exists(Path.Combine(dir.FullName, m))
    );
}
