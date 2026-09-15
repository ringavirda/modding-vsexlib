using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>Reads the bay-occupancy catalogue - every domain's <c>config/bayoccupancy/*.json</c> -
/// and populates <see cref="BayOccupancyRegistry"/> from it.</summary>
public sealed class BayOccupancyLoader
  : ContributedCatalogueLoader<BayOccupancySet, BayOccupancyRegistry> {
  /// <summary>The asset path every domain's rules are read from.</summary>
  public const string CataloguePath = "config/bayoccupancy/";

  private static readonly BayOccupancyLoader _instance = new();

  // Root keys BayOccupancySet.TryParse reads; item keys each entry reads.
  private static readonly HashSet<string> RootKeys =
  [
    "schema",
    "store",
    "items",
  ];
  private static readonly HashSet<string> ItemKeys = ["item", "cells"];

  private BayOccupancyLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "bayoccupancy";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. (root["items"].AsArray() ?? []).SelectMany(item =>
        JsonKeyAudit.UnknownKeys(item, ItemKeys)
      ),
    ];

  protected override bool TryParse(
    JsonObject root,
    out BayOccupancySet set,
    out string? error
  ) {
    bool ok = BayOccupancySet.TryParse(
      root,
      out BayOccupancySet? parsed,
      out error
    );
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    BayOccupancyRegistry registry,
    BayOccupancySet set
  ) => registry.Contribute(set);

  protected override int CountEntries(BayOccupancySet set) => set.Rules.Count;

  protected override CatalogueContributors Contributors(
    BayOccupancyRegistry registry
  ) => BayOccupancyRegistry.Contributors;

  protected override void Clear(BayOccupancyRegistry registry) =>
    registry.Clear();

  /// <summary>Parses the catalogue from already-read <c>(source, json)</c> pairs; a malformed file is
  /// reported and skipped.</summary>
  public static List<BayOccupancySet> Parse(
    IEnumerable<(string Source, string Json)> files,
    out List<string> errors
  ) => _instance.ParseFiles(files, out errors);

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents (the
  /// shared registry when null); returns one message per malformed file or clash.</summary>
  public static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    BayOccupancyRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? BayOccupancyRegistry.Shared);

  /// <summary>Reads every domain's catalogue out of the asset manager.</summary>
  public static List<(string Source, string Json)> Read(ICoreAPI api) =>
    _instance.ReadAssets(api);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code contributors;
  /// call from <c>ExpandedLibModSystem.AssetsFinalize</c>.</summary>
  public static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, BayOccupancyRegistry.Shared);
}
