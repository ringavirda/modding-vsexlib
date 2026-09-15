using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;

namespace ExpandedLib.Catalogues;

/// <summary>
/// The merged catalogue of every <see cref="ProcessRoute"/> in the world, keyed by stock family.
/// Merging is by (thickness, accepting family), a stage's address. World-free; runs headless.
/// </summary>
public sealed class ProcessRouteRegistry {
  /// <summary>The process-wide catalogue. Repopulated at <c>AssetsFinalize</c>.</summary>
  public static ProcessRouteRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by <see cref="ProcessRouteLoader"/>
  /// after its JSON read on every <c>Load(ICoreAPI)</c>.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  private readonly ExKeyedRegistry<ProcessRoute> _byFamily = new(r => r.Family);

  /// <summary>Merges <paramref name="route"/> into the family it names. Returns one message per
  /// clash, empty when the contribution was taken whole.</summary>
  public IReadOnlyList<string> Contribute(ProcessRoute route) {
    if (!_byFamily.TryGet(route.Family, out ProcessRoute? merged)) {
      _byFamily.Register(route with { Schema = ProcessRoute.CurrentSchema });
      return [];
    }

    var conflicts = new List<string>();
    string? shape = merged.Shape;
    if (route.Shape != null && shape != null && route.Shape != shape)
      conflicts.Add(
        $"{route.Family}: shape '{route.Shape}' clashes with '{shape}'; a family's stages must all be "
          + "addressable from one file, so the first one stands"
      );
    shape ??= route.Shape;

    var stages = merged.Stages.ToList();
    foreach (ProcessStage incoming in route.Stages)
      foreach (string family in incoming.AcceptedBy)
        Absorb(stages, incoming, family, route.Family, conflicts);

    _byFamily.Register(merged with { Shape = shape, Stages = [.. stages] });
    return conflicts;
  }

  // One (thickness, family) pair of an incoming stage, against the stages already merged.
  private static void Absorb(
    List<ProcessStage> stages,
    ProcessStage incoming,
    string family,
    string stockFamily,
    List<string> conflicts
  ) {
    int occupied = stages.FindIndex(s =>
      ProcessRoute.SameThickness(s.Thickness, incoming.Thickness)
      && s.IsAcceptedBy(family)
    );
    if (occupied >= 0) {
      ProcessStage held = stages[occupied];
      if (held.Element != incoming.Element || held.Code != incoming.Code)
        conflicts.Add(
          $"{stockFamily} {incoming.Thickness} '{family}': declared as "
            + $"{Describe(incoming)} but already drawn as {Describe(held)}; the first one stands"
        );
      return;
    }

    // Not claimed for this family. An identical stage is the same rung seen from another machine.
    int twin = stages.FindIndex(s =>
      ProcessRoute.SameThickness(s.Thickness, incoming.Thickness)
      && s.Element == incoming.Element
      && s.Code == incoming.Code
    );
    if (twin >= 0)
      stages[twin] = stages[twin] with {
        AcceptedBy = [.. stages[twin].AcceptedBy, family],
      };
    else
      stages.Add(incoming with { AcceptedBy = [family] });
  }

  private static string Describe(ProcessStage stage) =>
    $"'{stage.Element ?? "-"}' -> '{stage.Code ?? "-"}'";

  /// <summary>The merged route for <paramref name="family"/>, or null when nothing has claimed it.</summary>
  public ProcessRoute? Route(string? family) =>
    family != null && _byFamily.TryGet(family, out ProcessRoute? route)
      ? route
      : null;

  /// <summary>Looks up a family's merged route; <c>false</c> when nothing has claimed it.</summary>
  public bool TryGet(string family, out ProcessRoute? route) =>
    _byFamily.TryGet(family, out route);

  /// <summary>The stock families with a route.</summary>
  public IReadOnlyCollection<string> Families => _byFamily.Codes;

  /// <summary>Drops every family. The loader clears before repopulating on each world load.</summary>
  public void Clear() => _byFamily.Clear();
}
