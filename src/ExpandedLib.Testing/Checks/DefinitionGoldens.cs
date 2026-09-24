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

namespace ExpandedLib.Testing;

/// <summary>
/// Golden-file oracle for code-first definition parity: every def reproduces its golden
/// (<see cref="CheckGolden"/>), and the golden set exactly covers the defs
/// (<see cref="CheckCompleteness"/>).
/// </summary>
public static class DefinitionGoldens {
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
  /// against its committed golden under <paramref name="goldenRoot"/>.</summary>
  /// <returns><c>(true, "")</c> on match, else a readable diff message.</returns>
  public static (bool ok, string message) CheckGolden(
    string domain,
    Assembly asm,
    string relativePath,
    string goldenRoot
  ) {
    IExDef def = Collect(domain, asm)
      .Single(d => RelativePath(d) == relativePath);
    string file = FullPath(goldenRoot, def);
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

  /// <summary>Completeness of the golden set: <c>missing</c> is defs with no golden file,
  /// <c>orphans</c> is golden files no def claims.</summary>
  public static (
    IReadOnlyList<string> missing,
    IReadOnlyList<string> orphans
  ) CheckCompleteness(string domain, Assembly asm, string goldenRoot) {
    var defs = Collect(domain, asm);
    var claimed = defs.Select(d => Path.GetFullPath(FullPath(goldenRoot, d)))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var missing = defs.Where(d => !File.Exists(FullPath(goldenRoot, d)))
      .Select(RelativePath)
      .OrderBy(p => p)
      .ToList();

    string domainRoot = Path.Combine(goldenRoot, domain);
    var orphans = (
      Directory.Exists(domainRoot)
        ? Directory.EnumerateFiles(
          domainRoot,
          "*.json",
          SearchOption.AllDirectories
        )
        : []
    )
      .Where(f => !claimed.Contains(Path.GetFullPath(f)))
      .Select(f => Path.GetRelativePath(goldenRoot, f).Replace('\\', '/'))
      .OrderBy(p => p)
      .ToList();

    return (missing, orphans);
  }

  /// <summary>Re-blesses the goldens under <paramref name="goldenRoot"/> from the current def
  /// output: every golden when <c>EXLIB_WRITE_GOLDENS</c> is <c>1</c>, else those whose
  /// <see cref="RelativePath"/> contains one of its comma-separated fragments. Opt-in: call only
  /// when <see cref="WriteRequested"/>.</summary>
  /// <remarks>A fragment whose first <c>/</c>-separated segment is another domain and whose
  /// second is a golden category of this suite (the second segment of a
  /// <see cref="RelativePath"/> here, such as <c>blocktypes</c>) belongs to another assembly's
  /// goldens, is skipped here and never throws. A fragment naming a domain no suite has, with such
  /// a category, is skipped the same way.</remarks>
  /// <exception cref="InvalidOperationException">Any other fragment matches none of
  /// <paramref name="domain"/>'s goldens; the message names the value and the fragment and says a
  /// fragment starts with its domain, and nothing is written.</exception>
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
  ) {
    IReadOnlyList<string> only = WriteFilter(value);
    IReadOnlyList<IExDef> defs = Collect(domain, asm);
    var categories = defs
      .Select(d => RelativePath(d).Split('/')[1])
      .ToHashSet(StringComparer.Ordinal);

    string[] unmatched =
    [
      .. only.Where(f =>
        !IsOtherSuites(f, domain, categories)
        && !defs.Any(d => RelativePath(d).Contains(f, StringComparison.Ordinal))
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
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);
      File.WriteAllText(file, def.ToJson().ToString());
    }
  }

  private static bool IsOtherSuites(
    string fragment,
    string domain,
    HashSet<string> categories
  ) {
    string[] segments = fragment.Split('/');
    return segments.Length > 1
      && segments[0].Length > 0
      && segments[0] != domain
      && categories.Contains(segments[1]);
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
