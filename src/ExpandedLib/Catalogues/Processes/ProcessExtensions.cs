using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Catalogues;

/// <summary>
/// The public C# route into the process registries, for a mod that computes a spec at load. No
/// wider than the JSON schema.
/// </summary>
/// <param name="Routes">The sequence registry contributions land in.</param>
/// <param name="Jobs">The terminal registry contributions land in.</param>
public sealed record ProcessExtensions(
  ProcessRouteRegistry Routes,
  ProcessJobRegistry Jobs
) {
  /// <summary>The process-wide surface - the registries the loaders fill and every machine reads.</summary>
  public static ProcessExtensions Shared { get; } =
    new(ProcessRouteRegistry.Shared, ProcessJobRegistry.Shared);

  /// <summary>
  /// Adds <paramref name="stages"/> to the route for <paramref name="family"/>, merging exactly as a
  /// declared file does: an unclaimed (thickness, accepting family) pair is added, one already drawn the
  /// same way is a no-op, and one redrawn differently is returned as a conflict with the first standing.
  /// </summary>
  /// <returns>One human-readable message per clash; empty when the contribution was taken whole.</returns>
  /// <exception cref="ArgumentException">The stages do not form a valid route.</exception>
  public IReadOnlyList<string> AddStages(
    string family,
    IEnumerable<ProcessStage> stages,
    string? shape = null
  ) {
    var route = new ProcessRoute(
      ProcessRoute.CurrentSchema,
      family,
      shape,
      [.. stages]
    );
    Validate(route);
    return Routes.Contribute(route);
  }

  /// <summary>
  /// Adds <paramref name="jobs"/> to <paramref name="machine"/>'s table, merging exactly as a declared file
  /// does: a second job on one input is returned as a conflict and the first stands.
  /// </summary>
  /// <returns>One human-readable message per clash; empty when the contribution was taken whole.</returns>
  /// <exception cref="ArgumentException">A job is malformed by the same rules a declared one meets.</exception>
  public IReadOnlyList<string> AddJobs(
    string machine,
    IEnumerable<ProcessJob> jobs
  ) {
    var set = new ProcessJobSet(
      ProcessJobSet.CurrentSchema,
      machine,
      [.. jobs]
    );
    Validate(set);
    return Jobs.Contribute(set);
  }

  // Builds the declaration a file holds and runs it through the same TryParse.
  private static void Validate(ProcessRoute route) {
    if (
      !ProcessRoute.TryParse(
        ExJson.Of(
          new {
            schema = route.Schema,
            family = route.Family,
            shape = route.Shape,
            stages = route.Stages.Select(s => new {
              thickness = s.Thickness,
              element = s.Element,
              acceptedBy = s.AcceptedBy,
              code = s.Code,
              generate = s.Generate,
              formerCodes = s.FormerCodes,
            }),
          }
        ),
        out _,
        out string? error
      )
    )
      throw new ArgumentException(error, nameof(route));
  }

  private static void Validate(ProcessJobSet set) {
    if (
      !ProcessJobSet.TryParse(
        ExJson.Of(
          new {
            schema = set.Schema,
            machine = set.Machine,
            jobs = set.Jobs.Select(j => new {
              input = j.Input,
              output = j.Output,
              count = j.Count,
              stage = j.Stage ?? -1f,
              family = j.Family,
              minTorque = j.MinTorque,
            }),
          }
        ),
        out _,
        out string? error
      )
    )
      throw new ArgumentException(error, nameof(set));
  }
}
