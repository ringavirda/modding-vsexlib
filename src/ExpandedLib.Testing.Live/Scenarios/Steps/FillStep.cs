using System;
using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>fill</c>: every cell of the inclusive box set to the block by the world. A cell
/// above the ground that is not air fails the step before anything is set, unless it already
/// holds that block.</summary>
internal sealed class FillStep : ScenarioStep {
  public override string Kind => "fill";

  public override string What(SetupStep step) =>
    $"{step.Body["code"]} {step.Body["from"]?.ToString(Formatting.None)} {step.Body["to"]?.ToString(Formatting.None)}";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    Block block = ctx.BlockOf((string)o["code"]!);
    BlockPos from = ctx.Position(step, o["from"], "from");
    BlockPos to = ctx.Position(step, o["to"], "to");
    var cells = new List<BlockPos>();
    for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
      for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++) {
          var cell = new BlockPos(x, y, z);
          if (y >= 0 && !ctx.IsAir(cell) && ctx.Setup.BlockAt(cell).Id != block.Id)
            throw new StepFailure(
              $"{FailureDetail.Cell(cell)} holds {ctx.Setup.BlockAt(cell).Code}, not air.",
              Suspect.Scenario
            );
          cells.Add(cell);
        }
    foreach (BlockPos cell in cells)
      if (ctx.Setup.BlockAt(cell).Id != block.Id)
        ctx.SetBlock(block, cell);
    if (step.Id != null)
      ctx.Setup.Place(step.Id, new SetupPlacement(cells[0].Copy(), cells));
    report.Value = $"{cells.Count} cell(s) of {block.Code}";
    yield break;
  }
}
