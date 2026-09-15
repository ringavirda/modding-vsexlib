using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Pins the five-step <c>Load</c> order - clear, read, merge, invoke contributors, report - of
/// the three hand-rolled catalogue loaders, checked at source level.
/// </summary>
public class HandRolledLoaderOrderTests {
  // Each step marker is a substring unique to that loader's own spelling of the step.
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

  /// <summary>Isolates one loader's <c>Load(ICoreAPI)</c> method body, between its signature
  /// and its closing brace.</summary>
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
