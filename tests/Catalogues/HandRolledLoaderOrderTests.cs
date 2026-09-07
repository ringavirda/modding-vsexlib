using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The three hand-rolled catalogue loaders that do not derive from
/// <see cref="ContributedCatalogueLoader{TSet, TRegistry}"/> - <c>LiquidCatalogueLoader</c>,
/// <c>MaterialRoleLoader</c> and industry's <c>MetalCatalogueLoader</c> - keep the same five-step
/// <c>Load</c> shape: clear the registry, read the assets, merge them in, invoke the code
/// contributors, return the report. Checked at source level, by the order the marker for each step
/// first appears in the method, so a reordering fails here instead of drifting silently.
/// </summary>
public class HandRolledLoaderOrderTests {
  // One loader's Load(ICoreAPI) source, and the five step markers in their expected order - each a
  // substring unique to that loader's own spelling of the step (the merge step in particular is a
  // bare loop for liquids and a named call for the other two).
  private sealed record Loader(
    string Name,
    string RelativePath,
    string[] StepMarkers
  );

  private static readonly Loader[] Loaders = [
    new Loader(
      "LiquidCatalogueLoader",
      "src/Catalogues/Fluids/LiquidCatalogueLoader.cs",
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
      "src/Catalogues/Materials/MaterialRoleLoader.cs",
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
      "industry/Metals/MetalCatalogueLoader.cs",
      [
        "MetalRegistry.Clear();",
        "AssetCatalogueLoader.Read<MetalDef>",
        "int registered = Populate(",
        "MetalRegistry.Contributors.Invoke(api, api.Logger);",
        "return new CatalogueLoadReport(",
      ]
    ),
  ];

  private static readonly string[] StepNames = [
    "clear",
    "read",
    "merge",
    "Contributors.Invoke",
    "report",
  ];

  [Fact]
  public void Each_loader_clears_reads_merges_invokes_contributors_then_reports_in_order() {
    foreach (Loader loader in Loaders) {
      string text = File.ReadAllText(
        Path.Combine(RepoPaths.Root, loader.RelativePath)
      );

      int previous = -1;
      for (int step = 0; step < loader.StepMarkers.Length; step++) {
        string marker = loader.StepMarkers[step];
        int at = text.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.True(
          at >= 0,
          $"{loader.Name}: step '{StepNames[step]}' marker not found: \"{marker}\""
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
