using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing.Live;

/// <summary><c>complete</c>: the layout part. Every cell the structure's block entity reports
/// missing is raised with the first registered block matching the wanted code, then the step
/// waits for the entity's own monitor tick to see the structure whole.</summary>
internal sealed class CompleteStep : ScenarioStep {
  private static readonly AssetLocation AirCode = new("game:air");

  public override string Kind => "complete";

  public override string What(SetupStep step) => (string)step.Body["of"]!;

  public override string? Refuse(SetupStep step) {
    var o = (JObject)step.Body;
    if (o["materials"] != null)
      return "\"materials\" belongs to construction stages, which need the scenario player.";
    if (
      o["to"] is { } to
      && !(to.Type == JTokenType.String && (string?)to == "all")
    )
      return "\"to\" a stage needs the scenario player.";
    return null;
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    BlockPos cell = ctx.Position(step, SetupPositionOf(step), "of");
    foreach (bool wait in Raise(ctx, cell, report))
      yield return wait;
  }

  private static JToken SetupPositionOf(SetupStep step) =>
    new JObject { ["of"] = step.Body["of"] };

  /// <summary>Raises the missing layout cells of the structure at plot-local
  /// <paramref name="cell"/>; yields while it waits for the monitor tick.</summary>
  /// <exception cref="StepFailure">A wanted code matches no block, or the structure is not
  /// whole when the wait ends.</exception>
  public static IEnumerable<bool> Raise(
    ScenarioContext ctx,
    BlockPos cell,
    StepReport report
  ) {
    if (
      ctx.Setup.BlockEntityAt(cell)
      is not BlockEntityMultiblockStructure structure
    ) {
      report.Value = "no layout to complete";
      yield break;
    }
    var missing = new List<BlockEntityMultiblockStructure.MissingCell>();
    structure.IncompleteBlockCount(missing.Add);
    Dictionary<BlockPos, (int X, int Y, int Z)> authored =
      structure.LayoutCells.ToDictionary(c => c.At, c => c.Local);
    var chosen = new List<string>();
    foreach (var wanted in missing) {
      BlockPos local = ctx.Setup.Local(wanted.At);
      if (
        WildcardUtil.Match(wanted.Wanted, AirCode)
        || ctx.Placed.Contains(local)
        || (
          authored.TryGetValue(wanted.At, out var offset)
          && structure.Connectors.OutwardFacesAt(offset).Count > 0
        )
      )
        continue;
      Block block = Choose(ctx, wanted.Wanted);
      ctx.SetBlock(block, local);
      chosen.Add(block.Code.ToString());
    }
    report.Value =
      chosen.Count == 0
        ? "nothing to raise"
        : "raised "
          + string.Join(
            ", ",
            chosen.GroupBy(c => c).Select(g => $"{g.Key} x{g.Count()}")
          );

    double deadline = ctx.Now + 2 * structure.CompletionTickMs / 1000.0 + 1;
    while (!structure.StructureComplete && ctx.Now < deadline)
      yield return true;
    if (!structure.StructureComplete) {
      var still = new List<string>();
      structure.IncompleteBlockCount(m =>
        still.Add(
          $"{FailureDetail.Cell(ctx.Setup.Local(m.At))} wants {m.Wanted}, holds {m.Actual.Code}"
        )
      );
      throw new StepFailure(
        $"structure incomplete after the monitor tick: {still.Count} cell(s) missing: {string.Join("; ", still.Take(5))}"
      );
    }
  }

  private static Block Choose(ScenarioContext ctx, AssetLocation wanted) {
    var code = new AssetLocation(
      wanted.Domain,
      ExWildcards.FirstAlternative(wanted.Path)
    );
    Block? block = code.Path.Contains('*')
      ? ctx.World.SearchBlocks(code).FirstOrDefault(b => b.Id != 0)
      : ctx.World.GetBlock(code);
    return block is { Id: not 0 }
      ? block
      : throw new StepFailure(
        $"no registered block matches \"{wanted}\".",
        Suspect.Scenario
      );
  }
}
