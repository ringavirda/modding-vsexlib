using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ExpandedLib.Testing;

/// <summary>Resolves the Vintage Story game assemblies at runtime from the matching game
/// install.</summary>
public static class VsAssemblyResolver {
  private static readonly object Gate = new();
  private static bool _registered;

  private static readonly string? InstallKey = Metadata("GameInstallEnv");
  private static readonly string? GameSlug = Metadata("GameSlug");

  private static string? Metadata(string key) =>
    typeof(VsAssemblyResolver)
      .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .FirstOrDefault(a => a.Key == key)
      ?.Value;

  /// <summary>The resolved game install for this TFM (env var or <c>.game/&lt;slug&gt;</c>).</summary>
  /// <returns>Null if neither yields a path.</returns>
  public static string? InstallPath => ResolveInstallPath();

  /// <summary>Idempotently hooks <see cref="AppDomain.AssemblyResolve"/> to probe the game folders.</summary>
  public static void Register() {
    lock (Gate) {
      if (_registered)
        return;
      _registered = true;
    }

    string? vs = ResolveInstallPath();
    if (string.IsNullOrEmpty(vs))
      return;

    string[] dirs = [vs, Path.Combine(vs, "Lib"), Path.Combine(vs, "Mods")];

    AppDomain.CurrentDomain.AssemblyResolve += (_, args) => {
      string? name = new AssemblyName(args.Name).Name;
      if (name == null)
        return null;
      foreach (string dir in dirs) {
        string path = Path.Combine(dir, name + ".dll");
        if (File.Exists(path))
          return Assembly.LoadFrom(path);
      }
      return null;
    };
  }

  /// <summary>The install path for this TFM: the <see cref="InstallKey"/> environment variable if
  /// set, otherwise the in-repo install at <c>.game/&lt;slug&gt;</c>.</summary>
  private static string? ResolveInstallPath() {
    if (!string.IsNullOrEmpty(InstallKey)) {
      string? fromEnv = Environment.GetEnvironmentVariable(InstallKey);
      if (!string.IsNullOrEmpty(fromEnv))
        return fromEnv;
    }
    return FindRepoGameInstall();
  }

  /// <summary>Walks up from the test output directory looking for a provisioned
  /// <c>.game/&lt;slug&gt;</c> holding the game assemblies.</summary>
  private static string? FindRepoGameInstall() {
    if (string.IsNullOrEmpty(GameSlug))
      return null;
    for (
      DirectoryInfo? dir = new(AppContext.BaseDirectory);
      dir != null;
      dir = dir.Parent
    ) {
      string candidate = Path.Combine(dir.FullName, ".game", GameSlug);
      if (File.Exists(Path.Combine(candidate, "VintagestoryAPI.dll")))
        return candidate;
    }
    return null;
  }
}

internal static class HarnessModuleInitializer {
  // Live before any harness type that references the game assemblies is touched.
#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Init() => VsAssemblyResolver.Register();
}
