using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>stand</c>: puts the scenario player's feet in the cell <c>at</c> (<see cref="ScenarioPlayer.StandAt"/>).
/// The player stays there until a later step moves or parks it, so a <c>select</c> after it acts from that
/// cell.</summary>
internal sealed class StandStep : ScenarioStep {
  public override string Kind => "stand";

  public override string What(SetupStep step) => "stand";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"stand\" needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, step.Body["at"], "at");
    BlockPos at = ctx.Setup.Absolute(local);
    player.StandAt(at);
    report.Value = $"feet at {local.X},{local.Y},{local.Z}";
    yield break;
  }
}
