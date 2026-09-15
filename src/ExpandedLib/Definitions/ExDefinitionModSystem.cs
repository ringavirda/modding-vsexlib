using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Definitions;

/// <summary>Injects every registered code-first block, item and recipe definition into the
/// server's asset manager as synthetic assets for the vanilla object loader to build.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExDefinitionModSystem : ModSystem {
  // Server only: the object loader that consumes blocktypes is server-side.
  public override bool ShouldLoad(EnumAppSide side) =>
    side == EnumAppSide.Server;

  // Must run before the object loader (0.2) and after base assets are indexed (0).
  public override double ExecuteOrder() => 0.04;

  public override void AssetsLoaded(ICoreAPI api) {
    if (api.Side != EnumAppSide.Server)
      return;

    ExDefinitions.RunContributors(api);

    var origin = new ExDefinitionOrigin();

    // Read from the catalogue assets; ProcessRouteRegistry is only populated at AssetsFinalize.
    var routes = ProcessRouteLoader.Parse(
      ProcessRouteLoader.Read(api),
      out var routeErrors
    );
    var generated = ProcessItemEmitter.Emit(routes, out var skipped);
    foreach (ExItemDef def in generated)
      ExDefinitions.RegisterItem(def);

    foreach (string error in routeErrors)
      api.Logger.Error("[exlib] invalid stage route - " + error);
    foreach (string note in skipped)
      api.Logger.Notification(
        "[exlib] stage names a code it does not build - " + note
      );

    foreach (ExBlockDef def in ExDefinitions.Blocks)
      foreach (string key in Audit(def))
        api.Logger.Warning(
          "[exlib] {0}: root key '{1}' is not a blocktype key the game reads",
          def.QualifiedCode,
          key
        );
    foreach (ExItemDef def in ExDefinitions.Items)
      foreach (string key in Audit(def))
        api.Logger.Warning(
          "[exlib] {0}: root key '{1}' is not an itemtype key the game reads",
          def.Domain + ":" + def.Code,
          key
        );

    int blocks = 0;
    foreach (var (location, asset) in ExDefinitions.BuildBlockAssets(origin)) {
      api.Assets.Add(location, asset);
      blocks++;
    }

    int items = 0;
    foreach (var (location, asset) in ExDefinitions.BuildItemAssets(origin)) {
      api.Assets.Add(location, asset);
      items++;
    }

    int recipes = 0;
    foreach (var (location, asset) in ExDefinitions.BuildRecipeAssets(origin)) {
      api.Assets.Add(location, asset);
      recipes++;
    }

    ExDefinitions.RecordInjected(
      ExDefinitions
        .Blocks.Select(d => d.Location)
        .Concat(ExDefinitions.Items.Select(d => d.Location))
        .Concat(ExDefinitions.Recipes.Select(d => d.Location))
    );

    if (blocks > 0)
      api.Logger.Notification(
        "[exlib] Injected {0} code-first block definition(s).",
        blocks
      );
    if (items > 0)
      api.Logger.Notification(
        "[exlib] Injected {0} code-first item definition(s).",
        items
      );
    if (recipes > 0)
      api.Logger.Notification(
        "[exlib] Injected {0} code-first recipe file(s).",
        recipes
      );
  }

  /// <summary>The root keys of <paramref name="def"/>'s emitted JSON that are not a key the block
  /// loader reads. Empty when every key is known.</summary>
  internal static IReadOnlyList<string> Audit(ExBlockDef def) =>
    UnknownRootKeys(def.ToJson(), KnownRootKeys.IsKnownBlockKey);

  /// <summary>Item-side sibling of <see cref="Audit(ExBlockDef)"/>.</summary>
  internal static IReadOnlyList<string> Audit(ExItemDef def) =>
    UnknownRootKeys(def.ToJson(), KnownRootKeys.IsKnownItemKey);

  private static IReadOnlyList<string> UnknownRootKeys(
    JObject json,
    System.Func<string, bool> isKnown
  ) =>
    json.Properties()
      .Select(p => p.Name)
      .Where(key => !isKnown(key) && !IsByTypeSelector(key))
      .ToList();

  // A key ending "byType" is resolved generically by RegistryObjectType.solveByType, never a real
  // root key on its own; never flagged as unknown.
  private static bool IsByTypeSelector(string key) =>
    key.EndsWith("byType", System.StringComparison.OrdinalIgnoreCase);
}
