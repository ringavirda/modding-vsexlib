using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>Reads the craft-sequence catalogue, every domain's <c>config/craftsequences/*.json</c>, and
/// populates <see cref="CraftSequenceRegistry"/> from it.</summary>
public sealed class CraftSequenceLoader
  : ContributedCatalogueLoader<CraftSequenceSet, CraftSequenceRegistry> {
  /// <summary>The asset path every domain's sequences are read from.</summary>
  public const string CataloguePath = "config/craftsequences/";

  private static readonly CraftSequenceLoader _instance = new();

  // The root keys CraftSequenceSet.TryParse reads, and the keys each tool default, sequence, step and byproduct reads.
  private static readonly HashSet<string> RootKeys =
  [
    "schema",
    "machine",
    "sequences",
    "tools",
  ];
  private static readonly HashSet<string> ToolKeys =
  [
    "tool",
    "animation",
    "sound",
    "stroke",
    "strike",
  ];
  private static readonly HashSet<string> SequenceKeys =
  [
    "recipe",
    "steps",
    "byproducts",
  ];
  private static readonly HashSet<string> StepKeys =
  [
    "tool",
    "offhand",
    "seconds",
    "animation",
    "help",
    "at",
    "sound",
  ];
  private static readonly HashSet<string> ByproductKeys =
  [
    "type",
    "code",
    "quantity",
  ];

  private CraftSequenceLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "craftsequences";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. (root["sequences"].AsArray() ?? []).SelectMany(SequenceUnknownKeys),
      .. (root["tools"].AsArray() ?? []).SelectMany(tool =>
        JsonKeyAudit.UnknownKeys(tool, ToolKeys)
      ),
    ];

  private static IEnumerable<string> SequenceUnknownKeys(JsonObject sequence) =>
    [
      .. JsonKeyAudit.UnknownKeys(sequence, SequenceKeys),
      .. (sequence["steps"].AsArray() ?? []).SelectMany(step =>
        JsonKeyAudit.UnknownKeys(step, StepKeys)
      ),
      .. (sequence["byproducts"].AsArray() ?? []).SelectMany(by =>
        JsonKeyAudit.UnknownKeys(by, ByproductKeys)
      ),
    ];

  protected override bool TryParse(
    JsonObject root,
    out CraftSequenceSet set,
    out string? error
  ) {
    bool ok = CraftSequenceSet.TryParse(
      root,
      out CraftSequenceSet? parsed,
      out error
    );
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    CraftSequenceRegistry registry,
    CraftSequenceSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(CraftSequenceSet set) =>
    set.Sequences.Length;

  protected override CatalogueContributors Contributors(
    CraftSequenceRegistry registry
  ) => CraftSequenceRegistry.Contributors;

  protected override void Clear(CraftSequenceRegistry registry) =>
    registry.Clear();

  /// <summary>Parses the catalogue out of already-read <c>(source, json)</c> pairs. A malformed
  /// file is reported and skipped. Asset-free; runs headless.</summary>
  public static List<CraftSequenceSet> Parse(
    IEnumerable<(string Source, string Json)> files,
    out List<string> errors
  ) => _instance.ParseFiles(files, out errors);

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null).</summary>
  public static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    CraftSequenceRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? CraftSequenceRegistry.Shared);

  /// <summary>Reads every domain's catalogue out of the asset manager.</summary>
  public static List<(string Source, string Json)> Read(ICoreAPI api) =>
    _instance.ReadAssets(api);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  public static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, CraftSequenceRegistry.Shared);
}
