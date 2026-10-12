using System.Collections.Generic;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing.Live;

/// <summary>What a running step reports besides passing: the value its ok line prints.</summary>
internal sealed class StepReport {
  public string? Value { get; set; }
}

/// <summary>One step kind. <see cref="Run"/> is an iterator the runner advances once per server
/// tick: it yields to wait for the next tick, returns when the step has passed, and throws
/// <see cref="StepFailure"/> when it has not.</summary>
internal abstract class ScenarioStep {
  public abstract string Kind { get; }

  /// <summary>The short form of the step its output line names.</summary>
  public virtual string What(SetupStep step) => step.Id ?? "";

  /// <summary>Why the live runner cannot run this step as written, or null.</summary>
  public virtual string? Refuse(SetupStep step) => null;

  public abstract IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  );
}
