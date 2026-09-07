using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The three hand-rolled catalogue loaders that do not derive from
/// <see cref="ContributedCatalogueLoader{TSet, TRegistry}"/> - <c>LiquidCatalogueLoader</c>,
/// <c>MaterialRoleLoader</c> and industry's <c>MetalCatalogueLoader</c> - keep the same five-step
/// <c>Load</c> shape: clear the registry, read the assets, merge them in, invoke the code
/// contributors, return the report. Compared at source level, within each loader's own
/// <c>Load(ICoreAPI)</c> method body, by the order in which the calls appear there, so a reordering
/// fails here instead of drifting silently.
/// </summary>
public class HandRolledLoaderOrderTests {
  // One loader's absolute path, and the five step markers in their expected order - each a
  // substring unique to that loader's own spelling of the step (the merge step in particular is a
  // bare loop for liquids and a named call for the other two).
  private sealed record Loader(
    string Name,
    string AbsolutePath,
    string[] StepMarkers
  );

  private static readonly Loader[] Loaders =
  [
    new Loader(
      "LiquidCatalogueLoader",
      Path.Combine(
        RepoPaths.Src("exlib"),
        "Catalogues/Fluids/LiquidCatalogueLoader.cs"
      ),
      [
        "ExLiquids.Clear();",
        "AssetCatalogueLoader.Read<LiquidCatalogue>",
        "for (int i = 0; i < read.Items.Count; i++)",
        "ExLiquids.Contributors.Invoke(api, api.Logger);",
        "return new CatalogueLoadReport(",
      ]
    ),
    new Loader(
      "MaterialRoleLoader",
      Path.Combine(
        RepoPaths.Src("exlib"),
        "Catalogues/Materials/MaterialRoleLoader.cs"
      ),
      [
        "MaterialRoleRegistry.Clear();",
        "AssetCatalogueLoader.Read<MaterialRoleCatalogue>",
        "int registered = Overlay(",
        "MaterialRoleRegistry.InvokeContributors(api);",
        "return new CatalogueLoadReport(",
      ]
    ),
    new Loader(
      "MetalCatalogueLoader",
      Path.Combine(
        RepoPaths.Root,
        "src/ExpandedLib.Industry/Metals/MetalCatalogueLoader.cs"
      ),
      [
        "MetalRegistry.Clear();",
        "AssetCatalogueLoader.Read<MetalDef>",
        "int registered = Populate(",
        "MetalRegistry.Contributors.Invoke(api, api.Logger);",
        "return new CatalogueLoadReport(",
      ]
    ),
  ];

  private static readonly string[] StepNames =
  [
    "clear",
    "read",
    "merge",
    "Contributors.Invoke",
    "report",
  ];

  private const string LoadSignature =
    "public static CatalogueLoadReport Load(ICoreAPI api) {";

  /// <summary>Isolates one loader's <c>Load(ICoreAPI)</c> method body - the lines strictly between its
  /// signature and the closing brace back at the method's own two-space indent - so a marker in a doc
  /// comment or in another member cannot satisfy the order check.</summary>
  private static string LoadMethodBody(string path) {
    string[] lines = File.ReadAllLines(path);
    int start = System.Array.FindIndex(lines, l => l.Contains(LoadSignature));
    Assert.True(start >= 0, $"{path}: no 'Load(ICoreAPI)' method found.");

    int end = start + 1;
    while (end < lines.Length && lines[end] != "  }")
      end++;
    Assert.True(end < lines.Length, $"{path}: Load(ICoreAPI) never closes.");

    return string.Join('\n', lines[(start + 1)..end]);
  }

  [Fact]
  public void Each_loader_clears_reads_merges_invokes_contributors_then_reports_in_order() {
    foreach (Loader loader in Loaders) {
      string body = LoadMethodBody(loader.AbsolutePath);

      int previous = -1;
      for (int step = 0; step < loader.StepMarkers.Length; step++) {
        string marker = loader.StepMarkers[step];
        int at = body.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.True(
          at >= 0,
          $"{loader.Name}: step '{StepNames[step]}' marker not found in Load: \"{marker}\""
        );
        Assert.True(
          at > previous,
          $"{loader.Name}: step '{StepNames[step]}' runs out of order (before an earlier step)."
        );
        previous = at;
      }
    }
  }
}
