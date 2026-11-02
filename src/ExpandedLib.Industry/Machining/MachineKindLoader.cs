using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Machining;

/// <summary>Reads the machine kind catalogue, every domain's <c>config/machinekinds/*.json</c>,
/// and populates <see cref="MachineKindRegistry"/> from it.</summary>
internal sealed class MachineKindLoader
  : ContributedCatalogueLoader<MachineKindSet, MachineKindRegistry> {
  internal const string CataloguePath = "config/machinekinds/";

  private static readonly MachineKindLoader _instance = new();

  private static readonly HashSet<string> RootKeys = ["schema", "kinds"];
  private static readonly HashSet<string> KindKeys =
  [
    "code",
    "ratedSpeed",
    "standing",
    "fixture",
    "anchors",
    "steps",
  ];
  private static readonly HashSet<string> AnchorKeys =
  [
    "origin",
    "rotation",
    "scale",
  ];
  private static readonly HashSet<string> StepKeys =
  [
    "motion",
    "power",
    "tooling",
    "anchor",
    "envelope",
  ];
  private static readonly HashSet<string> EnvelopeKeys = ["across", "length"];

  private MachineKindLoader() { }

  protected override string AssetPath => CataloguePath;
  protected override string CatalogueName => "machinekinds";

  protected override IReadOnlyList<string> UnknownKeys(JsonObject root) =>
    [
      .. JsonKeyAudit.UnknownKeys(root, RootKeys),
      .. (root["kinds"].AsArray() ?? []).SelectMany(kind =>
        JsonKeyAudit
          .UnknownKeys(kind, KindKeys)
          .Concat(
            Members(kind["anchors"])
              .SelectMany(a => JsonKeyAudit.UnknownKeys(a, AnchorKeys))
          )
          .Concat(
            Members(kind["steps"])
              .SelectMany(step =>
                JsonKeyAudit
                  .UnknownKeys(step, StepKeys)
                  .Concat(
                    JsonKeyAudit.UnknownKeys(step["envelope"], EnvelopeKeys)
                  )
              )
          )
      ),
    ];

  private static IEnumerable<JsonObject> Members(JsonObject node) =>
    node.Token is JObject obj ? obj.Properties().Select(p => node[p.Name]) : [];

  protected override bool TryParse(
    JsonObject root,
    out MachineKindSet set,
    out string? error
  ) {
    bool ok = MachineKindSet.TryParse(
      root,
      out MachineKindSet? parsed,
      out error
    );
    set = parsed!;
    return ok;
  }

  protected override IReadOnlyList<string> Contribute(
    MachineKindRegistry registry,
    MachineKindSet set
  ) => registry.Contribute(set);

  protected override int CountEntries(MachineKindSet set) => set.Kinds.Count;

  protected override CatalogueContributors Contributors(
    MachineKindRegistry registry
  ) => MachineKindRegistry.Contributors;

  protected override void Clear(MachineKindRegistry registry) =>
    registry.Clear();

  /// <summary>Parses <paramref name="files"/> and replaces <paramref name="registry"/>'s contents
  /// with them (the shared registry when null). Asset-free; runs headless.</summary>
  internal static List<string> Load(
    IEnumerable<(string Source, string Json)> files,
    MachineKindRegistry? registry = null
  ) => _instance.MergeFiles(files, registry ?? MachineKindRegistry.Shared);

  /// <summary>Reads the catalogue, repopulates the shared registry and runs its code
  /// contributors.</summary>
  internal static CatalogueLoadReport Load(ICoreAPI api) =>
    _instance.LoadCatalogue(api, MachineKindRegistry.Shared);
}
