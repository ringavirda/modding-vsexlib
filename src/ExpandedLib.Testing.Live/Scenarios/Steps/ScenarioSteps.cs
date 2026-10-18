using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing.Live;

/// <summary>The table of step kinds the live runner runs, and the sequence that runs a list of
/// them.</summary>
internal static class ScenarioSteps {
  private static readonly Dictionary<string, ScenarioStep> Table =
    new ScenarioStep[]
    {
      new PlaceStep(),
      new RunStep(),
      new CompleteStep(),
      new FillStep(),
      new SetStep(),
      new CallStep(),
      new InsertStep(),
      new ExpectStep(),
      new WaitStep(),
      new RepeatStep(),
      new InteractStep(),
      new WindowStep(),
      new IgniteStep(),
      new CraftStep(),
      new BreakStep(),
      new AdvanceStep(),
      new SelectStep(),
    }.ToDictionary(s => s.Kind);

  public static IEnumerable<string> Kinds => Table.Keys;

  /// <summary>Why the live runner cannot run <paramref name="step"/> or a step under it, or
  /// null when it can run all of them.</summary>
  public static string? Refusal(SetupStep step) {
    foreach (SetupStep each in step.All()) {
      if (!Table.TryGetValue(each.Kind, out ScenarioStep? impl))
        return $"{each.Path}: \"{each.Kind}\" is not a step the live runner runs yet.";
      if (impl.Refuse(each) is { } reason)
        return $"{each.Path}: {reason}";
    }
    return null;
  }

  /// <summary>The short form of <paramref name="step"/> its output line names.</summary>
  public static string What(SetupStep step) => Table[step.Kind].What(step);

  /// <summary>Runs <paramref name="steps"/> in order. Each step's output line is
  /// <paramref name="prefix"/> and its 1-based index.</summary>
  /// <exception cref="StepFailure">A step failed; its FAIL line and detail are already
  /// printed.</exception>
  public static IEnumerable<bool> Run(
    ScenarioContext ctx,
    IReadOnlyList<SetupStep> steps,
    string prefix
  ) {
    for (int i = 0; i < steps.Count; i++) {
      SetupStep step = steps[i];
      ScenarioStep impl = Table[step.Kind];
      string label = prefix + (i + 1);
      string what = impl.What(step);
      ctx.Current = label;
      var report = new StepReport();
      ctx.Resolved.Clear();
      using IEnumerator<bool> running = PackLedger
        .Accounted(ctx, step, impl.Run(ctx, step, label, report))
        .GetEnumerator();
      while (true) {
        bool more;
        StepFailure? failure = null;
        try {
          more = running.MoveNext();
          ctx.ThrowIfLogged();
        } catch (StepFailure e) {
          more = false;
          failure = e;
        } catch (SetupReadingException e) {
          more = false;
          failure = new StepFailure(e.Message, Suspect.Mod, e);
        } catch (SetupFileException e) {
          more = false;
          failure = new StepFailure(e.Message, Suspect.Scenario, e);
        } catch (System.Exception e) {
          more = false;
          failure = new StepFailure(Describe(e), Suspect.Mod, e);
        }
        if (failure != null) {
          if (!failure.Reported) {
            failure.Reported = true;
            ctx.Line(
              ScenarioOutput.StepFail(label, step.Kind, what, failure.Message)
            );
            foreach (string line in FailureDetail.Lines(ctx, step, failure))
              ctx.Line(ScenarioOutput.Detail(line));
          }
          throw failure;
        }
        if (!more)
          break;
        yield return true;
      }
      ctx.Line(ScenarioOutput.StepOk(label, step.Kind, what, report.Value));
    }
  }

  /// <summary>An exception as a failure prints it: type, message and its first stack
  /// frames.</summary>
  public static string Describe(System.Exception e) {
    System.Exception inner = e
      is System.Reflection.TargetInvocationException { InnerException: { } i }
      ? i
      : e;
    string[] frames =
    [
      .. (inner.StackTrace ?? "")
        .Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
        .Take(3)
        .Select(f => f.Trim()),
    ];
    return $"{inner.GetType().Name}: {inner.Message} ({string.Join(" | ", frames)})";
  }
}
