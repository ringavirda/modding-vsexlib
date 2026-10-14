using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>Every step a setup case runs.</summary>
internal static class SetupStepWalk {
  /// <summary>Every step of <paramref name="model"/> with a repeat's body under it: the main
  /// <c>steps</c>, each <c>during</c> action's, and each variant's replaced, <c>after</c> (which
  /// covers the steps run after layout entries) and own <c>during</c> steps. <see cref="SetupStep.Path"/> names where the file wrote each.</summary>
  public static IEnumerable<SetupStep> Of(SetupFileModel model) =>
    model
      .Steps.Concat(model.During.SelectMany(d => d.Steps))
      .Concat(
        model.Variants.SelectMany(v =>
          v.Replace.Values.Concat(v.After.SelectMany(a => a.Steps))
            .Concat(v.During?.SelectMany(d => d.Steps) ?? [])
        )
      )
      .SelectMany(s => s.All());
}
