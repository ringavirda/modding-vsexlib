using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing.Live;

/// <summary>One case from its first step to its verdict: the steps in order, the settle wait,
/// and the skipped, step and detail lines. <see cref="Run"/> is advanced once per server tick.</summary>
internal sealed class ScenarioCaseRun(
  ScenarioCase scenario,
  ScenarioContext ctx
) {
  /// <summary>Whether the case passed; meaningful once <see cref="Run"/> has finished.</summary>
  public bool Passed { get; private set; }

  /// <summary>The step the case failed at, as its fail line prints it.</summary>
  public int FailedStep { get; private set; }

  public string Suspect { get; private set; } = Live.Suspect.Mod;

  /// <summary>Server seconds from the first step to the verdict.</summary>
  public double Seconds { get; private set; }

  public IEnumerable<bool> Run() {
    IReadOnlyList<SetupStep> steps = scenario.Model.Steps;
    double start = ctx.Now;
    StepFailure? failure = null;
    int failedAt = 0;
    RunStep.PlayerPlace? place = ctx.Player is { } player ? player.Place : null;
    IEnumerable<bool> layout = scenario.Model.Drawn is { } drawn
      ? SetupLayoutWalk.Run(ctx, drawn, place)
      : Enumerable.Empty<bool>();
    using (
      IEnumerator<bool> running = layout
        .Concat(ScenarioSteps.Run(ctx, steps, ""))
        .GetEnumerator()
    ) {
      while (failure == null) {
        bool more;
        try {
          more = running.MoveNext();
        } catch (StepFailure e) {
          failure = e;
          failedAt = ctx.TopLevel;
          break;
        }
        if (!more)
          break;
        if (ctx.Now - start > scenario.Timeout) {
          failedAt = ctx.TopLevel;
          failure = Report(
            failedAt,
            "case",
            "timeout",
            $"still running after {scenario.Timeout} s."
          );
          break;
        }
        yield return true;
      }
    }

    if (failure != null) {
      for (int i = failedAt; i < steps.Count; i++)
        ctx.Line(
          ScenarioOutput.Skipped(
            (i + 1).ToString(),
            steps[i].Kind,
            ScenarioSteps.What(steps[i])
          )
        );
    } else {
      double end = ctx.Now + scenario.Settle;
      while (failure == null) {
        try {
          ctx.ThrowIfLogged();
        } catch (StepFailure e) {
          failedAt = steps.Count + 1;
          failure = Report(
            failedAt,
            "settle",
            $"{scenario.Settle} s",
            e.Message,
            e.Suspect
          );
          break;
        }
        PackLedger.GiveSpawns(ctx);
        if (ctx.Now >= end)
          break;
        yield return true;
      }
      if (failure == null) {
        try {
          PackLedger.Finish(ctx);
        } catch (StepFailure e) {
          failedAt = steps.Count + 1;
          failure = Report(failedAt, "pack", "ends", e.Message, e.Suspect);
        }
      }
    }

    Seconds = ctx.Now - start;
    Passed = failure == null;
    FailedStep = failedAt;
    if (failure != null)
      Suspect = failure.Suspect;
  }

  private StepFailure Report(
    int step,
    string kind,
    string what,
    string reason,
    string suspect = Live.Suspect.Mod
  ) {
    var failure = new StepFailure(reason, suspect) { Reported = true };
    ctx.Line(ScenarioOutput.StepFail(step.ToString(), kind, what, reason));
    foreach (string line in FailureDetail.Lines(ctx, null, failure))
      ctx.Line(ScenarioOutput.Detail(line));
    return failure;
  }
}
