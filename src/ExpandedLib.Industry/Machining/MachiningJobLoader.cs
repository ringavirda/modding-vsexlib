using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Machining;

/// <summary>Reads the machining job catalogue, every domain's <c>config/machiningjobs/*.json</c>,
/// and populates <see cref="MachiningJobRegistry"/> from it.</summary>
internal sealed class MachiningJobLoader
  : ContributedCatalogueLoader<MachiningJobSet, MachiningJobRegistry> {
  internal const string CataloguePath = "config/machiningjobs/";

  private static readonly MachiningJobLoader _instance = new();

  private static readonly HashSet<string> RootKeys = ["schema", "jobs"];
  private static readonly HashSet<string> JobKeys =
  [
    "step",
    "input",
    "output",
    "count",
    "material",
    "removed",
    "energy",
    "minTier",
    "wear",
    "fixture",
    "machines",
    "byproducts",
    "hand",
  ];
  private static readonly HashSet<string> ByproductKeys = ["code", "count"];
  private static readonly HashSet<string> HandKeys = ["machine", "recipe"];

  private MachiningJobLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "machiningjobs";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. (root["jobs"].AsArray() ?? []).SelectMany(job =>
        JsonKeyAudit
          .UnknownKeys(job, JobKeys)
          .Concat(JsonKeyAudit.UnknownKeys(job["hand"], HandKeys))
          .Concat(
            (job["byproducts"].AsArray() ?? []).SelectMany(byproduct =>
              JsonKeyAudit.UnknownKeys(byproduct, ByproductKeys)
            )
          )
      ),
    ];

  protected override bool TryParse(
    JsonObject root,
    out MachiningJobSet set,
    out string? error
  ) {
    bool ok = MachiningJobSet.TryParse(
      root,
      out MachiningJobSet? parsed,
      out error
    );
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    MachiningJobRegistry registry,
    MachiningJobSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(MachiningJobSet set) => set.Jobs.Count;

  protected override CatalogueContributors Contributors(
    MachiningJobRegistry registry
  ) => MachiningJobRegistry.Contributors;

  protected override void Clear(MachiningJobRegistry registry) =>
    registry.Clear();

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null). Asset-free; runs headless.</summary>
  internal static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    MachiningJobRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? MachiningJobRegistry.Shared);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  internal static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, MachiningJobRegistry.Shared);
}
