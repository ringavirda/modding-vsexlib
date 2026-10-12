using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>expect</c>: the readings the setup model parses (<c>block</c>, <c>member</c>,
/// <c>tree</c>, <c>network</c>), a footprint, and two cells in one network.</summary>
internal sealed class ExpectStep : ScenarioStep {
  public override string Kind => "expect";

  public override string What(SetupStep step) {
    string text = step.Body.ToString(Formatting.None);
    return text.Length <= 80 ? text : text[..77] + "...";
  }

  public override string? Refuse(SetupStep step) {
    var o = (JObject)step.Body;
    if (o["info"] != null)
      return "expect \"info\" needs the scenario player's language.";
    if (o["drops"] != null)
      return "expect \"drops\" needs the scenario player's break.";
    return null;
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    if (o["at"] != null)
      ctx.Expected.Add(ctx.Position(step, o["at"], "at"));
    if (o["footprint"] != null)
      report.Value = Footprint(ctx, o);
    else if (o["same"] != null)
      report.Value = Same(ctx, step, o);
    else {
      var reading = new ReadingContext(ctx.Setup);
      foreach (ReadingTest test in step.Tests)
        if (!test.Holds(reading))
          throw new StepFailure(
            $"read {test.Describe(reading)}, expected {o.ToString(Formatting.None)}"
          );
      report.Value = string.Join(
        "; ",
        step.Tests.Select(t => t.Describe(reading))
      );
    }
    yield break;
  }

  private static string Footprint(ScenarioContext ctx, JObject o) {
    string mode = (string)o["footprint"]!;
    SetupPlacement placed =
      ctx.Setup.Placed((string)o["of"]!)
      ?? throw new StepFailure(
        $"\"{o["of"]}\" has placed nothing.",
        Suspect.Scenario
      );
    SetupFrame frame =
      placed.Frame
      ?? throw new StepFailure(
        $"\"{o["of"]}\" is no exlib structure.",
        Suspect.Scenario
      );
    Block principal = ctx.Setup.BlockAt(frame.Principal);
    BlockPos principalAbsolute = ctx.Setup.Absolute(frame.Principal);
    List<BlockPos> cells =
    [
      frame.Principal,
      .. frame.Footprint.Select(c => c.Pos),
    ];
    foreach (BlockPos cell in cells.Distinct()) {
      ctx.Touched.Add(cell.Copy());
      bool holds = mode switch {
        "air" => ctx.IsAir(cell),
        "filled" => ctx.Setup.BlockAt(cell).Id == principal.Id
          || (
            ctx.Setup.BlockEntityAt(cell)
              is BlockEntityStructureFiller { Principal: { } p }
            && p.Equals(principalAbsolute)
          ),
        _ => throw new StepFailure(
          $"footprint is \"filled\" or \"air\", not \"{mode}\".",
          Suspect.Scenario
        ),
      };
      if (!holds)
        throw new StepFailure(
          $"footprint cell {FailureDetail.Cell(cell)} holds {ctx.Setup.BlockAt(cell).Code}, expected {mode}."
        );
    }
    return $"{cells.Distinct().Count()} cell(s) {mode}";
  }

  private static string Same(ScenarioContext ctx, SetupStep step, JObject o) {
    string type =
      (string?)o["network"]
      ?? throw new StepFailure("\"same\" takes \"network\".", Suspect.Scenario);
    BlockPos at = ctx.Position(step, o["at"], "at");
    BlockPos other = ctx.Position(step, o["same"], "same");
    object? first = ctx.Setup.NetworkAt(at, type);
    object? second = ctx.Setup.NetworkAt(other, type);
    if (first == null || !ReferenceEquals(first, second))
      throw new StepFailure(
        $"{FailureDetail.Cell(at)} and {FailureDetail.Cell(other)} are not in one {type} network."
      );
    return $"one {type} network";
  }
}
