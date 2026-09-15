using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>Reads the stage catalogue, every domain's <c>config/processroutes/*.json</c>, and
/// populates <see cref="ProcessRouteRegistry"/> from it.</summary>
public sealed class ProcessRouteLoader
  : ContributedCatalogueLoader<ProcessRoute, ProcessRouteRegistry> {
  /// <summary>The asset path every domain's routes are read from.</summary>
  public const string CataloguePath = "config/processroutes/";

  private static readonly ProcessRouteLoader _instance = new();

  // The root keys ProcessRoute.TryParse reads, and the keys each stage entry reads.
  private static readonly HashSet<string> RootKeys =
  [
    "schema",
    "family",
    "shape",
    "stages",
  ];
  private static readonly HashSet<string> StageKeys =
  [
    "thickness",
    "acceptedBy",
    "halfStep",
    "code",
    "generate",
    "formerCodes",
    "element",
  ];

  private ProcessRouteLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "processroutes";

  // Root keys plus every stage's keys.
  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. (root["stages"].AsArray() ?? []).SelectMany(stage =>
        JsonKeyAudit.UnknownKeys(stage, StageKeys)
      ),
    ];

  protected override bool TryParse(
    JsonObject root,
    out ProcessRoute set,
    out string? error
  ) {
    bool ok = ProcessRoute.TryParse(root, out ProcessRoute? route, out error);
    set = route!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    ProcessRouteRegistry registry,
    ProcessRoute set
  ) => registry.Contribute(set);

  protected override int CountEntries(ProcessRoute set) => set.Stages.Length;

  protected override CatalogueContributors Contributors(
    ProcessRouteRegistry registry
  ) => ProcessRouteRegistry.Contributors;

  protected override void Clear(ProcessRouteRegistry registry) =>
    registry.Clear();

  /// <summary>Parses the catalogue out of already-read <c>(source, json)</c> pairs. A malformed
  /// file is reported and skipped. Asset-free; runs headless.</summary>
  public static List<ProcessRoute> Parse(
    IEnumerable<(string Source, string Json)> files,
    out List<string> errors
  ) => _instance.ParseFiles(files, out errors);

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null).</summary>
  public static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    ProcessRouteRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? ProcessRouteRegistry.Shared);

  /// <summary>Reads every domain's catalogue out of the asset manager.</summary>
  public static List<(string Source, string Json)> Read(ICoreAPI api) =>
    _instance.ReadAssets(api);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  public static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, ProcessRouteRegistry.Shared);
}
