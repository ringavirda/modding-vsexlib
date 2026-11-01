using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Forming;

/// <summary>Reads the stock form catalogue, every domain's <c>config/stockforms/*.json</c>, and
/// populates <see cref="StockFormRegistry"/> from it.</summary>
internal sealed class StockFormLoader
  : ContributedCatalogueLoader<StockFormSet, StockFormRegistry> {
  internal const string CataloguePath = "config/stockforms/";

  private static readonly StockFormLoader _instance = new();

  private static readonly HashSet<string> RootKeys =
  [
    "schema",
    "forms",
    "feedstock",
  ];
  private static readonly HashSet<string> FormKeys =
  [
    "name",
    "baseWidth",
    "baseThickness",
    "maxWidth",
    "baseLength",
    "spreadExponent",
    "shape",
    "baseElement",
    "formerNames",
  ];
  private static readonly HashSet<string> FeedstockKeys =
  [
    "offered",
    "entersAs",
  ];

  private StockFormLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "stockforms";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) {
    var unknown = new List<string>(JsonKeyAudit.UnknownKeys(root, RootKeys));
    foreach (JsonObject form in root["forms"].AsArray() ?? [])
      unknown.AddRange(JsonKeyAudit.UnknownKeys(form, FormKeys));
    foreach (JsonObject entry in root["feedstock"].AsArray() ?? [])
      unknown.AddRange(JsonKeyAudit.UnknownKeys(entry, FeedstockKeys));
    return unknown;
  }

  protected override bool TryParse(
    JsonObject root,
    out StockFormSet set,
    out string? error
  ) {
    bool ok = StockFormSet.TryParse(root, out StockFormSet? parsed, out error);
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    StockFormRegistry registry,
    StockFormSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(StockFormSet set) =>
    set.Forms.Count + set.Feedstock.Count;

  protected override CatalogueContributors Contributors(
    StockFormRegistry registry
  ) => StockFormRegistry.Contributors;

  protected override void Clear(StockFormRegistry registry) => registry.Clear();

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null). Asset-free; runs headless.</summary>
  internal static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    StockFormRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? StockFormRegistry.Shared);

  /// <summary>Reads the catalogue, repopulates the shared registry, runs its code contributors and
  /// logs the form and feedstock counts held once they have run.</summary>
  internal static CatalogueLoadReport Load(ICoreAPI api) {
    CatalogueLoadReport report = _instance.LoadCatalogue(
      api,
      StockFormRegistry.Shared
    );
    api.Logger.Notification(
      "[exlib] stockforms: {0} form(s), {1} feedstock after contributors",
      StockFormRegistry.Shared.All.Count,
      StockFormRegistry.Shared.Feedstock.Count
    );
    return report;
  }
}
