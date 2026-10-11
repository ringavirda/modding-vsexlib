using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing.Live;

/// <summary><c>wait</c>: lets the server run for <c>seconds</c>, or until a condition holds
/// within <c>max</c> seconds. <c>hold</c> fails the step the moment it stops holding.</summary>
internal sealed class WaitStep : ScenarioStep {
  public override string Kind => "wait";

  public override string What(SetupStep step) =>
    step.Body is JObject o
      ? string.Join(" ", o.Properties().Select(p => p.Name))
      : $"{step.Body} s";

  public override string? Refuse(SetupStep step) {
    if (step.Body is not JObject o)
      return null;
    if (o["until"] != null && o["max"] == null)
      return "\"until\" takes \"max\".";
    if (o["until"] == null && o["seconds"] == null)
      return "wait takes seconds, or until with max.";
    return null;
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    double seconds = 0;
    double max = 0;
    IReadOnlyList<ReadingTest> until = [];
    IReadOnlyList<ReadingTest> hold = [];
    if (step.Body is JObject o) {
      var scope = new ReadingScope(null, false, false, new SetupReferences());
      SetupJson.Where where = ctx.Where(step);
      if (o["until"] != null) {
        until = ReadingTests.ParseList(o["until"], where.In("until"), scope);
        max = (double)o["max"]!;
      } else
        seconds = (double)o["seconds"]!;
      if (o["hold"] != null)
        hold = ReadingTests.ParseList(o["hold"], where.In("hold"), scope);
    } else
      seconds = (double)step.Body;

    var reading = new ReadingContext(ctx.Setup);
    double start = ctx.Now;
    double end = start + (until.Count > 0 ? max : seconds);
    while (true) {
      foreach (ReadingTest test in hold)
        if (!test.Holds(reading))
          throw new StepFailure(
            $"hold broke after {Elapsed(ctx, start)} s: {test.Describe(reading)}"
          );
      if (until.Count > 0 && until.All(t => t.Holds(reading))) {
        report.Value =
          $"held after {Elapsed(ctx, start)} s: "
          + string.Join("; ", until.Select(t => t.Describe(reading)));
        yield break;
      }
      if (ctx.Now >= end) {
        if (until.Count > 0)
          throw new StepFailure(
            $"not met in {max.ToString(CultureInfo.InvariantCulture)} s; last read {string.Join("; ", until.Select(t => t.Describe(reading)))}"
          );
        report.Value = $"{Elapsed(ctx, start)} s";
        yield break;
      }
      yield return true;
    }
  }

  private static string Elapsed(ScenarioContext ctx, double start) =>
    (ctx.Now - start).ToString("F1", CultureInfo.InvariantCulture);
}
