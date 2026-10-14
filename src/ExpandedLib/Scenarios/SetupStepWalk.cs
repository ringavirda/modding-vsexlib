using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>Every step a setup case runs.</summary>
internal static class SetupStepWalk {
  /// <summary>
  /// Every step of <paramref name="model"/> with the steps under it (a repeat's body): the main
  /// <c>steps</c>, the steps inserted after layout entries, each <c>during</c> action's steps, and
  /// each variant's replaced steps, <c>after</c> steps and own <c>during</c> actions. A step's
  /// <see cref="SetupStep.Path"/> names where the file wrote it.
  /// </summary>
  public static IEnumerable<SetupStep> Of(SetupFileModel model) =>
    model
      .Steps.Concat(model.AfterEntry.SelectMany(a => a.Steps))
      .Concat(model.During.SelectMany(d => d.Steps))
      .Concat(
        model.Variants.SelectMany(v =>
          v
            .Replace.Values.Concat(v.After.SelectMany(a => a.Steps))
            .Concat(v.During?.SelectMany(d => d.Steps) ?? [])
        )
      )
      .SelectMany(s => s.All());
}
