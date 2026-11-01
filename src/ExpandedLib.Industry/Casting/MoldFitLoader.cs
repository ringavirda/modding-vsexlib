using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Casting;

/// <summary>Reads the mould fit catalogue, every domain's <c>config/moldfits/*.json</c>, and populates
/// <see cref="MoldFitRegistry"/> from it.</summary>
internal sealed class MoldFitLoader
  : ContributedCatalogueLoader<MoldFitSet, MoldFitRegistry> {
  internal const string CataloguePath = "config/moldfits/";

  private static readonly MoldFitLoader _instance = new();

  private static readonly HashSet<string> RootKeys = ["schema", "oversize"];

  private MoldFitLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "moldfits";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    JsonKeyAudit.UnknownKeys(root, RootKeys);

  protected override bool TryParse(
    JsonObject root,
    out MoldFitSet set,
    out string? error
  ) {
    bool ok = MoldFitSet.TryParse(root, out MoldFitSet? parsed, out error);
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    MoldFitRegistry registry,
    MoldFitSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(MoldFitSet set) => set.Oversize.Count;

  protected override CatalogueContributors Contributors(
    MoldFitRegistry registry
  ) => MoldFitRegistry.Contributors;

  protected override void Clear(MoldFitRegistry registry) => registry.Clear();

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null). Asset-free; runs headless.</summary>
  internal static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    MoldFitRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? MoldFitRegistry.Shared);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  internal static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, MoldFitRegistry.Shared);
}
