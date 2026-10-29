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
    return null;
  }

  /// <summary>Sets <paramref name="block"/> at the plot-local <paramref name="local"/> and returns the
  /// cells it fills: <paramref name="local"/> first, then a megablock's footprint.</summary>
  /// <param name="code">The code the draw ledger names <paramref name="block"/> by.</param>
  /// <exception cref="StepFailure">A cell the block fills holds a block other than air at or above
  /// y 0.</exception>
  public static List<BlockPos> Put(
    ScenarioContext ctx,
    Block block,
    string code,
    BlockPos local
  ) {
    List<BlockPos> footprint =
    [
      .. block is IFillerHost host
        ? ctx
          .Setup.Footprint(host, local, host.StructureAngle)
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
    PackLedger.DrawBlock(ctx, code, local, local.ToString(), local);
    ctx.SetBlock(block, local);
    foreach (BlockPos cell in cells)
      ctx.Placed.Add(cell.Copy());
    return cells;
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
    List<BlockPos> cells = Put(ctx, block, code, local);
    if (step.Id != null)
      ctx.Setup.Place(
        step.Id,
        new SetupPlacement(local.Copy(), cells, ctx.Setup.FrameAt(local))
      );
    if (o["tree"] is JObject tree)
      SetStep.WriteTree(ctx, local, tree);
    report.Value = $"{block.Code} at plot {FailureDetail.Cell(local)}";
    if (o["complete"] != null)
      foreach (
        bool wait in CompleteStep.Complete(
          ctx,
          local,
          o["complete"],
          o["materials"],
          report
        )
      )
        yield return wait;
  }
}
