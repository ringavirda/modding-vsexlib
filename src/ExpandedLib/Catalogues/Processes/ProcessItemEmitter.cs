using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;

namespace ExpandedLib.Catalogues;

/// <summary>Builds an <see cref="ExItemDef"/> for every stopping point in the stage catalogue that
/// names a code and opts in to generation.</summary>
public static class ProcessItemEmitter {
  /// <summary>Domain never generated into: injecting an itemtype there would replace one of the
  /// base game's own items.</summary>
  private const string VanillaDomain = "game";

  // Fallback shape for a declaration that specifies none.
  private const string FallbackShape = "game:item/ingot";
  private const int DefaultStackSize = 64;

  /// <summary>Every item def the catalogue calls for, codes deduplicated. <paramref name="skipped"/>
  /// collects one message per stage that named a code and got no item.</summary>
  public static IEnumerable<ExItemDef> Emit(
    IEnumerable<ProcessRoute> routes,
    out List<string> skipped
  ) {
    skipped = [];
    return
    [
      .. Buildable(routes, skipped)
        .Select(b => Build(b.Route, b.Stage, b.Code)),
    ];
  }

  /// <summary>The codes <see cref="Emit"/> would build.</summary>
  public static IEnumerable<string> GeneratedCodes(
    IEnumerable<ProcessRoute> routes
  ) => [.. Buildable(routes, []).Select(b => b.Code.ToString())];

  // Every stopping point that opts in and resolves, deduplicated.
  private static List<(
    ProcessRoute Route,
    ProcessStage Stage,
    AssetLocation Code
  )> Buildable(IEnumerable<ProcessRoute> routes, List<string> skipped) {
    var buildable = new List<(ProcessRoute, ProcessStage, AssetLocation)>();
    var seen = new HashSet<string>();

    foreach (ProcessRoute route in routes)
      foreach (ProcessStage stage in route.Stages) {
        // A render-only intermediate is a state, not a thing; an opted-out one exists already.
        if (stage.Code == null || !stage.Generate)
          continue;

        string where = $"{route.Family} {stage.Thickness}";
        AssetLocation? code = Resolve(stage.Code);
        if (code == null || string.IsNullOrWhiteSpace(code.Path)) {
          skipped.Add($"{where}: '{stage.Code}' is not a usable item code");
          continue;
        }
        if (code.Domain == VanillaDomain) {
          skipped.Add(
            $"{where}: '{stage.Code}' is a vanilla code, so it is wired up rather than built; declare "
              + "\"generate\": false to say so explicitly"
          );
          continue;
        }
        if (!seen.Add(code.ToString()))
          continue;

        buildable.Add((route, stage, code));
      }

    return buildable;
  }

  // A malformed code is skipped.
  private static AssetLocation? Resolve(string code) {
    try {
      return new AssetLocation(code);
    } catch {
      return null;
    }
  }

  private static ExItemDef Build(
    ProcessRoute route,
    ProcessStage stage,
    AssetLocation code
  ) {
    ExItemDef def = ExItemDef
      .Create(code.Domain, code.Path)
      .MaxStackSize(DefaultStackSize)
      .CreativeCommon("*");

    // The family's shape file, drawn at this stage's element; no element is the whole file.
    def = def.Shape(route.Shape ?? FallbackShape);
    if (route.Shape != null && stage.Element != null)
      def = def.ShapeSelectiveElements(stage.Element);

    return def;
  }
}
