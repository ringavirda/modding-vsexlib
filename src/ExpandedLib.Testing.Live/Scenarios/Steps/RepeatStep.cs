using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing.Live;

/// <summary><c>repeat</c>: its steps <c>times</c> times, or until <c>until</c> holds after a
/// round, at most <c>max</c> rounds. With both, it stops at whichever comes first and does not
/// fail on <c>times</c> running out.</summary>
internal sealed class RepeatStep : ScenarioStep {
  private const int DefaultMax = 20;

  public override string Kind => "repeat";

  public override string What(SetupStep step) =>
    step.Body["times"] != null
      ? $"x{step.Body["times"]}"
      : $"until, max {step.Body["max"] ?? DefaultMax}";

  public override string? Refuse(SetupStep step) =>
    step.Body["times"] == null && step.Body["until"] == null
      ? "repeat takes \"times\" or \"until\"."
      : null;

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    int? times = (int?)o["times"];
    int max = (int?)o["max"] ?? DefaultMax;
    IReadOnlyList<ReadingTest> until = [];
    if (o["until"] != null)
      until = ReadingTests.ParseList(
        o["until"],
        ctx.Where(step).In("until"),
        new ReadingScope(null, false, false, new SetupReferences())
      );
    var reading = new ReadingContext(ctx.Setup);
    for (int round = 1; ; round++) {
      foreach (
        bool wait in ScenarioSteps.Run(ctx, step.Steps, $"{label}.{round}.")
      )
        yield return wait;
      if (until.Count > 0 && until.All(t => t.Holds(reading))) {
        report.Value = $"{round} round(s), until held";
        yield break;
      }
      if (round >= (times ?? max)) {
        if (times == null)
          throw new StepFailure(
            $"until not met after {max} round(s); last read {string.Join("; ", until.Select(t => t.Describe(reading)))}"
          );
        report.Value = $"{round} round(s)";
        yield break;
      }
    }
  }
}
