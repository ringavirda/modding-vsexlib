using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary>The block under a failed step's FAIL line that says enough to fix it without a
/// rerun (scenarios.md, When a step fails), minus the schematic and the teleport the runner
/// adds.</summary>
internal static class FailureDetail {
  private const int StackFrames = 6;
  private const int MaxCells = 40;

  public static IReadOnlyList<string> Lines(
    ScenarioContext ctx,
    SetupStep step,
    StepFailure failure
  ) {
    var lines = new List<string> {
      "step " + new JObject { [step.Kind] = step.Body }.ToString(Formatting.None),
    };
    foreach ((string key, BlockPos local) in ctx.Resolved)
      lines.Add(
        $"position {key}: plot {Cell(local)} absolute {Cell(ctx.Setup.Absolute(local))}"
      );
    foreach ((string id, SetupPlacement placed) in ctx.Setup.AllPlaced)
      if (placed.Frame is { } frame)
        lines.Add(
          $"frame {id}: principal {Cell(frame.Principal)} angle {frame.Angle}"
        );
    lines.Add($"suspect {failure.Suspect}");
    if (failure.InnerException is { } cause)
      lines.AddRange(
        (cause.StackTrace ?? "")
          .Split('\n', StringSplitOptions.RemoveEmptyEntries)
          .Take(StackFrames)
          .Select(f => "at " + f.Trim().Replace("at ", ""))
      );

    var cells = new List<BlockPos>();
    cells.AddRange(ctx.Touched);
    cells.AddRange(ctx.Resolved.Select(r => r.Local));
    foreach (SetupPlacement placed in ctx.Setup.AllPlaced.Values)
      if (placed.Frame is { } frame)
        cells.AddRange(frame.Footprint.Select(c => c.Pos));
    foreach (BlockPos local in cells.Distinct().Take(MaxCells))
      lines.Add(
        $"cell {Cell(local)} {ctx.Setup.BlockAt(local).Code} {ctx.Setup.BlockEntityAt(local)?.GetType().Name ?? "-"}"
      );

    foreach (BlockPos local in ctx.Expected)
      if (ctx.Setup.BlockEntityAt(local) is { } be) {
        var tree = new TreeAttribute();
        be.ToTreeAttributes(tree);
        lines.Add($"tree {Cell(local)} {tree.ToJsonToken()}");
      }

    foreach ((string id, SetupPlacement placed) in ctx.Setup.AllPlaced)
      if (ctx.Setup.BlockEntityAt(placed.Cell) is BlockEntityMultiblockStructure structure)
        structure.IncompleteBlockCount(m =>
          lines.Add(
            $"missing {id}: {Cell(ctx.Setup.Local(m.At))} wanted {m.Wanted} actual {m.Actual.Code}"
              + (m.OutwardFace == null ? "" : $" outward face {m.OutwardFace}")
          )
        );

    lines.AddRange(ctx.Errors.Errors.Select(e => "error " + e));
    lines.AddRange(ctx.Errors.Warnings.Select(w => "warning " + w));
    return lines;
  }

  public static string Cell(BlockPos pos) => $"[{pos.X}, {pos.Y}, {pos.Z}]";
}
