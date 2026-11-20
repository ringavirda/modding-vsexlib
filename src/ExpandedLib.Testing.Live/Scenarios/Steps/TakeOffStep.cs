using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>takeoff</c>: the scenario player takes a fixed piece off with its tool in the main hand
/// and the sneak held; the piece and the fasteners it returns go back to the pack.</summary>
internal sealed class TakeOffStep : ScenarioStep {
  public override string Kind => "takeoff";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

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
        "\"takeoff\" needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, o["at"], "at");
    string was = ctx.Setup.BlockAt(local).Code.ToString();
    ctx.RequireInWorld(local);
    if (player.TakeOff(ctx, ctx.Setup.Absolute(local), StepValues.Hit(o["hit"])) is { } failure)
      throw new StepFailure(
        $"the player could not take {was} off at {FailureDetail.Cell(local)}: {failure}.",
        Suspect.Mod
      );
    ctx.Touched.Add(local.Copy());
    report.Value = $"{was} off at plot {FailureDetail.Cell(local)}";
    yield break;
  }
}
