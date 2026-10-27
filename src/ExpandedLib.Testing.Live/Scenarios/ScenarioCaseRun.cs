using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing.Live;

/// <summary>One case from its first step to its verdict: the steps in order, the settle wait,
/// and the skipped, step and detail lines. <see cref="Run"/> is advanced once per server tick.</summary>
internal sealed class ScenarioCaseRun(
  ScenarioCase scenario,
  ScenarioContext ctx,
  SetupSampler? sampler = null
) {
  /// <summary>Whether the case passed; meaningful once <see cref="Run"/> has finished.</summary>
  public bool Passed { get; private set; }

  /// <summary>The step the case failed at, as its fail line prints it.</summary>
  public int FailedStep { get; private set; }

  /// <summary>The label the case's fail line names as its step: the step's number, or
  /// <c>c&lt;i&gt;</c> for the failing check.</summary>
  public string FailedLabel { get; private set; } = "0";

  public string Suspect { get; private set; } = Live.Suspect.Mod;

  /// <summary>Server seconds from the first step to the verdict.</summary>
  public double Seconds { get; private set; }

  public IEnumerable<bool> Run() {
    IReadOnlyList<SetupStep> steps = scenario.Model.Steps;
    double start = ctx.Now;
    StepFailure? failure = null;
    int failedAt = 0;
    string? failedLabel = null;
    RunStep.PlayerPlace? place = ctx.Player is { } player ? player.Place : null;
    RunStep.PlayerWrench? wrench = ctx.Player is { } turner
      ? turner.Wrench
      : null;
    IEnumerable<bool> layout = scenario.Model.Drawn is { } drawn
      ? SetupLayoutWalk.Run(ctx, drawn, place, wrench)
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
      if (sampler != null) {
        sampler.Start();
        while (sampler.Failure == null && !sampler.Done) {
          if (ctx.Now - start > scenario.Timeout) {
            failedAt = steps.Count + 1;
            failure = Report(
              failedAt,
              "run",
              "record",
              $"still running after {scenario.Timeout} s."
            );
            break;
          }
          yield return true;
          sampler.Advance();
        }
        sampler.Stop();
        if (failure == null && sampler.Failure is { } stopped) {
          failedAt = steps.Count + 1;
          failure = stopped.Reported
            ? stopped
            : Report(
              failedAt,
              "run",
              "record",
              stopped.Message,
              stopped.Suspect
            );
        }
        failure = Record(failure, steps.Count + 1);
        if (failure == null && sampler.Recording is { } recorded)
          failure = Judge(recorded, out failedLabel);
        if (failure != null)
          failedAt = steps.Count + 1;
      }
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
          PackLedger.Finish(ctx, scenario.Model.Fails != null);
        } catch (StepFailure e) {
          failedAt = steps.Count + 1;
          failure = Report(failedAt, "pack", "ends", e.Message, e.Suspect);
        }
      }
    }

    Seconds = ctx.Now - start;
    Passed = failure == null;
    FailedStep = failedAt;
    FailedLabel = failedLabel ?? failedAt.ToString();
    if (failure != null)
      Suspect = failure.Suspect;
  }

  private StepFailure? Record(StepFailure? failure, int step) {
    try {
      if (sampler!.Save() && failure == null)
        ctx.Line(
          ScenarioOutput.StepOk(
            "run",
            "record",
            $"{scenario.Model.Seconds} s, {sampler.RecordPath}",
            null
          )
        );
      return failure;
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
      return failure ?? Report(step, "run", "record", e.Message);
    }
  }

  private StepFailure? Judge(SetupRecording recording, out string? label) {
    label = null;
    IReadOnlyList<SetupVerdict> verdicts;
    try {
      verdicts = new SetupRun(scenario.Model, recording).Verdicts(
        ordersOnly: true
      );
    } catch (SetupReadingException e) {
      return Report(
        scenario.Model.Steps.Count + 1,
        "check",
        "readings",
        e.Message
      );
    }
    StepFailure? failure = null;
    foreach (SetupVerdict verdict in verdicts) {
      string at = $"c{verdict.Index + 1}";
      string id = verdict.Check.Id;
      if (failure != null)
        ctx.Line(ScenarioOutput.Skipped(at, "check", id));
      else if (verdict.Skipped)
        ctx.Line(
          ScenarioOutput.Skipped(
            at,
            "check",
            $"{id} (drawn order: judged in the harness only)"
          )
        );
      else if (verdict.Failure is { } failed) {
        label = at;
        failure = new StepFailure(failed.ToString(), Live.Suspect.Mod) {
          Reported = true,
        };
        ctx.Line(ScenarioOutput.StepFail(at, "check", id, failed.ToString()));
        ctx.Line(ScenarioOutput.Detail($"check {failed.Id}"));
        ctx.Line(ScenarioOutput.Detail($"says {failed.Says}"));
        ctx.Line(ScenarioOutput.Detail($"second {failed.Second}"));
        ctx.Line(ScenarioOutput.Detail($"readings {failed.Readings}"));
      } else
        ctx.Line(ScenarioOutput.StepOk(at, "check", id, null));
    }
    return failure;
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
