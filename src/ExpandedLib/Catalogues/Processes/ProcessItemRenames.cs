using System.Collections.Generic;
using ExpandedLib.Migrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Catalogues;

/// <summary>Rewrites held stacks of a stage product that has been renamed, from the
/// <c>formerCodes</c> the stage declares.</summary>
public class ProcessItemRenames : IItemCodeMigration {
  public string Name => "process stage products";

  /// <summary>The pairs the registry's current contents call for. Runs at server start, after
  /// <c>AssetsFinalize</c> has populated the registry.</summary>
  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  ) => Remaps(ProcessRouteRegistry.Shared.Families.Count == 0 ? [] : Routes());

  private static IEnumerable<ProcessRoute> Routes() {
    foreach (string family in ProcessRouteRegistry.Shared.Families)
      if (ProcessRouteRegistry.Shared.Route(family) is { } route)
        yield return route;
  }

  /// <summary>Every <c>(former, current)</c> pair <paramref name="routes"/> declares. A stage
  /// naming no code has nothing to be renamed to.</summary>
  public static IEnumerable<(AssetLocation Old, AssetLocation New)> Remaps(
    IEnumerable<ProcessRoute> routes
  ) {
    foreach (ProcessRoute route in routes)
      foreach (ProcessStage stage in route.Stages) {
        if (stage.Code == null)
          continue;
        foreach (string former in stage.FormerCodes)
          yield return (new AssetLocation(former), new AssetLocation(stage.Code));
      }
  }
}
