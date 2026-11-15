using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>cap</c>: the scenario player clicks a crossing of a fixed piece with the wrench, fitting
/// the def's cap from the off hand or taking off the cap the crossing carries, or expects the
/// server's refusal named by <c>fails</c>.</summary>
internal sealed class CapStep : ScenarioStep {
  public override string Kind => "cap";

  public override string What(SetupStep step) =>
    $"{step.Body["at"]?.ToString(Formatting.None)} {step.Body["face"]}";

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
    BlockFacing face = AssembleStep.Face((string?)o["face"]);
    string was = ctx.Setup.BlockAt(local).Code.ToString();
    ctx.RequireInWorld(local);
    string? failure = player.Cap(
      ctx,
      ctx.Setup.Absolute(local),
      face,
      out string? code
    );
    ctx.Touched.Add(local.Copy());
    string? fails = (string?)o["fails"];
    if (fails != null) {
      if (failure == null)
        throw new StepFailure(
          $"{was} at {FailureDetail.Cell(local)} took the click on face {face.Code}; the step expected the refusal {fails}.",
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
        $"the player could not cap {was} at {FailureDetail.Cell(local)} on face {face.Code}: {failure}.",
        Suspect.Mod
      );
    report.Value =
      $"{was} face {face.Code} at plot {FailureDetail.Cell(local)}";
  }
}
