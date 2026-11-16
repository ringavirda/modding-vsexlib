using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>dig</c>: the cell <c>at</c> or the inclusive box from <c>from</c> to <c>to</c> set to
/// air by the world, every cell checked against the file's ground before any is dug.</summary>
internal sealed class DigStep : ScenarioStep {
  public override string Kind => "dig";

  public override string What(SetupStep step) =>
    step.Body["at"] is { } at
      ? $"at {at.ToString(Formatting.None)}"
      : $"{step.Body["from"]?.ToString(Formatting.None)} {step.Body["to"]?.ToString(Formatting.None)}";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    BlockPos from = ctx.Position(
      step,
      o["at"] ?? o["from"],
      o["at"] != null ? "at" : "from"
    );
    BlockPos to = o["at"] != null ? from : ctx.Position(step, o["to"], "to");
    List<BlockPos> cells;
    try {
      cells = SetupDig.Cells(ctx.Model, from, to, ctx.Where(step));
    } catch (SetupFileException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
    foreach (BlockPos cell in cells)
      ctx.RequireInWorld(cell);
    SetupDig.Dig(ctx.Setup, cells);
    foreach (BlockPos cell in cells)
      ctx.Touched.Add(cell.Copy());
    if (step.Id != null)
      ctx.Setup.Place(step.Id, new SetupPlacement(cells[0].Copy(), cells));
    report.Value = $"{cells.Count} cell(s) dug";
    yield break;
  }
}
