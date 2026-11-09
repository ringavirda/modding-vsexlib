using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Casting;

/// <summary>Reads the casting station catalogue, every domain's <c>config/castingstations/*.json</c>,
/// and populates <see cref="CastingStationRegistry"/> from it.</summary>
internal sealed class CastingStationLoader
  : ContributedCatalogueLoader<CastingStationSet, CastingStationRegistry> {
  internal const string CataloguePath = "config/castingstations/";

  private static readonly CastingStationLoader _instance = new();

  private static readonly HashSet<string> RootKeys = ["schema", "stations"];
  private static readonly HashSet<string> StationKeys =
  [
    "code",
    "moulds",
    "sand",
  ];
  private static readonly HashSet<string> SandKeys = ["base", "half", "item"];

  private CastingStationLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "castingstations";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. (root["stations"].AsArray() ?? []).SelectMany(station =>
        JsonKeyAudit
          .UnknownKeys(station, StationKeys)
          .Concat(JsonKeyAudit.UnknownKeys(station["sand"], SandKeys))
      ),
    ];

  protected override bool TryParse(
    JsonObject root,
    out CastingStationSet set,
    out string? error
  ) {
    bool ok = CastingStationSet.TryParse(
      root,
      out CastingStationSet? parsed,
      out error
    );
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    CastingStationRegistry registry,
    CastingStationSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(CastingStationSet set) =>
    set.Stations.Count;

  protected override CatalogueContributors Contributors(
    CastingStationRegistry registry
  ) => CastingStationRegistry.Contributors;

  protected override void Clear(CastingStationRegistry registry) =>
    registry.Clear();

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null). Asset-free; runs headless.</summary>
  internal static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    CastingStationRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? CastingStationRegistry.Shared);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  internal static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, CastingStationRegistry.Shared);
}
