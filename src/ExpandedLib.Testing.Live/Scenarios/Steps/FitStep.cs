using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>fit</c>: the scenario player fits <c>stack</c>, held in the main hand with the def's
/// tool in the off hand, onto the placed block at <c>at</c> with a sneak click, or expects the
/// server's refusal named by <c>fails</c>.</summary>
internal sealed class FitStep : ScenarioStep {
  public override string Kind => "fit";

  public override string What(SetupStep step) =>
    $"{step.Body["at"]?.ToString(Formatting.None)} {step.Body["stack"]}";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"cap\" needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, o["at"], "at");
    string item =
      (string?)o["stack"]
      ?? throw new StepFailure("\"fit\" needs a stack.", Suspect.Scenario);
    string was = ctx.Setup.BlockAt(local).Code.ToString();
    ctx.RequireInWorld(local);
    string? failure = player.Fit(
      ctx,
      ctx.Setup.Absolute(local),
      item,
      out string? code
    );
    ctx.Touched.Add(local.Copy());
    string? fails = (string?)o["fails"];
    if (fails != null) {
      if (failure == null)
        throw new StepFailure(
          $"{was} at {FailureDetail.Cell(local)} took the fit of {item}; the step expected the refusal {fails}.",
          Suspect.Mod
        );
      if (code != fails)
        throw new StepFailure(
          $"{was} at {FailureDetail.Cell(local)} was refused with {failure}; the step expected {fails}.",
          Suspect.Mod
        );
      report.Value = $"refused: {failure}";
      yield break;
    }
    if (failure != null)
      throw new StepFailure(
        $"the player could not fit {item} onto {was} at {FailureDetail.Cell(local)}: {failure}.",
        Suspect.Mod
      );
    report.Value = $"{item} onto {was} at plot {FailureDetail.Cell(local)}";
  }
}
