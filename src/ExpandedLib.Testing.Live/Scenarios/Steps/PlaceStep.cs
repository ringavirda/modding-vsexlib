using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>place</c> by the world: the block accessor sets the block with a stack of it, so
/// its entity is created and a megablock fills its footprint.</summary>
internal sealed class PlaceStep : ScenarioStep {
  public override string Kind => "place";

  public override string What(SetupStep step) =>
    $"{step.Body["code"]} at {step.Body["at"]?.ToString(Formatting.None)}";

  public override string? Refuse(SetupStep step) {
    var o = (JObject)step.Body;
    if ((string?)o["by"] == "player")
      return "place by \"player\" needs the scenario player.";
    if (o["facing"] != null)
      return "\"facing\" is for place by \"player\".";
    if (o["materials"] != null)
      return "\"materials\" belongs to construction stages, which need the scenario player.";
    if (o["complete"] is { } c && !(c.Type == JTokenType.String && (string?)c == "all"))
      return "\"complete\" to a stage needs the scenario player.";
    return null;
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    string code = (string)o["code"]!;
    Block block = ctx.BlockOf(code);
    BlockPos local = ctx.Position(step, o["at"], "at");
    List<BlockPos> footprint =
    [
      .. block is IFillerHost host
        ? StructureFillers
          .FootprintCells(host, local, (block as BlockFilledMegastructure)?.StructureAngle ?? 0)
          .Select(c => c.Pos)
        : [],
    ];
    List<BlockPos> cells = [local, .. footprint.Where(c => !c.Equals(local))];
    foreach (BlockPos cell in cells)
      if (cell.Y >= 0 && !ctx.IsAir(cell))
        throw new StepFailure(
          $"{FailureDetail.Cell(cell)} holds {ctx.Setup.BlockAt(cell).Code}, not air.",
          Suspect.Scenario
        );
    ctx.SetBlock(block, local);
    foreach (BlockPos cell in cells)
      ctx.Placed.Add(cell.Copy());
    if (step.Id != null)
      ctx.Setup.Place(
        step.Id,
        new SetupPlacement(local.Copy(), cells, ctx.Setup.FrameAt(local))
      );
    if (o["tree"] is JObject tree)
      SetStep.WriteTree(ctx, local, tree);
    report.Value = $"{block.Code} at plot {FailureDetail.Cell(local)}";
    if (o["complete"] != null)
      foreach (bool wait in CompleteStep.Raise(ctx, local, report))
        yield return wait;
  }
}
