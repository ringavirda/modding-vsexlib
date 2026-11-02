using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Machining;

/// <summary>Reads the cutting material catalogue, every domain's <c>config/cuttingmaterials/*.json</c>,
/// and populates <see cref="CuttingMaterialRegistry"/> from it.</summary>
internal sealed class CuttingMaterialLoader
  : ContributedCatalogueLoader<CuttingMaterialSet, CuttingMaterialRegistry> {
  internal const string CataloguePath = "config/cuttingmaterials/";

  private static readonly CuttingMaterialLoader _instance = new();

  private static readonly HashSet<string> RootKeys =
  [
    "schema",
    "materials",
    "handPower",
  ];
  private static readonly HashSet<string> MaterialKeys =
  [
    "code",
    "density",
    "energy",
  ];
  private static readonly HashSet<string> MotionKeys =
  [
    "rotary",
    "reciprocating",
  ];

  private CuttingMaterialLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "cuttingmaterials";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. JsonKeyAudit.UnknownKeys(root["handPower"], MotionKeys),
      .. (root["materials"].AsArray() ?? []).SelectMany(material =>
        JsonKeyAudit
          .UnknownKeys(material, MaterialKeys)
          .Concat(JsonKeyAudit.UnknownKeys(material["energy"], MotionKeys))
      ),
    ];

  protected override bool TryParse(
    JsonObject root,
    out CuttingMaterialSet set,
    out string? error
  ) {
    bool ok = CuttingMaterialSet.TryParse(
      root,
      out CuttingMaterialSet? parsed,
      out error
    );
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    CuttingMaterialRegistry registry,
    CuttingMaterialSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(CuttingMaterialSet set) =>
    set.Materials.Count;

  protected override CatalogueContributors Contributors(
    CuttingMaterialRegistry registry
  ) => CuttingMaterialRegistry.Contributors;

  protected override void Clear(CuttingMaterialRegistry registry) =>
    registry.Clear();

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null). Asset-free; runs headless.</summary>
  internal static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    CuttingMaterialRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? CuttingMaterialRegistry.Shared);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  internal static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, CuttingMaterialRegistry.Shared);
}
