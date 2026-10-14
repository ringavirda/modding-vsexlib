using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing.Live;

/// <summary><c>complete</c>: the layout part raises every cell the structure's block entity reports
/// missing with the first registered block matching the wanted code, then waits for the entity's own
/// monitor tick to see the structure whole; the stage part walks the construction through the
/// scenario player.</summary>
internal sealed class CompleteStep : ScenarioStep {
  private static readonly AssetLocation AirCode = new("game:air");

  public override string Kind => "complete";

  public override string What(SetupStep step) => (string)step.Body["of"]!;

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    BlockPos cell = ctx.Position(step, SetupPositionOf(step), "of");
    foreach (bool wait in Complete(ctx, cell, o["to"], o["materials"], report))
      yield return wait;
  }

  private static JToken SetupPositionOf(SetupStep step) =>
    new JObject { ["of"] = step.Body["of"] };

  /// <summary>Brings the block at plot-local <paramref name="cell"/> up to <paramref name="to"/>:
  /// its layout when it is a multiblock structure, then its construction stages when it carries
  /// <see cref="ExRightClickConstructable"/>.</summary>
  /// <param name="to">The stage to stop at, or "all" or null for the last.</param>
  /// <param name="materials">The variant each stored wildcard key is supplied in, or null.</param>
  /// <exception cref="StepFailure">The block has neither part, a stage needs the scenario player
  /// that is not there, or a part does not complete.</exception>
  public static IEnumerable<bool> Complete(
    ScenarioContext ctx,
    BlockPos cell,
    JToken? to,
    JToken? materials,
    StepReport report
  ) {
    BlockEntity? entity = ctx.Setup.BlockEntityAt(cell);
    var construction = entity?.GetBehavior<ExRightClickConstructable>();
    var structure = entity as BlockEntityMultiblockStructure;
    if (structure == null && construction == null)
      throw new StepFailure(
        $"{FailureDetail.Cell(cell)} is neither a multiblock nor a construction, so it has nothing to complete.",
        Suspect.Scenario
      );
    if (structure != null)
      foreach (bool wait in Raise(ctx, cell, structure, report))
        yield return wait;
    if (construction != null) {
      PackLedger.DrawStages(ctx, construction.Construction, cell.ToString());
    }
    if (construction != null)
      foreach (
        bool wait in ConstructionWalk.Walk(
          ctx,
          cell,
          construction,
          to,
          materials,
          report
        )
      )
        yield return wait;
  }

  private static IEnumerable<bool> Raise(
    ScenarioContext ctx,
    BlockPos cell,
    BlockEntityMultiblockStructure structure,
    StepReport report
  ) {
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
      PackLedger.DrawBlock(
        ctx,
        WantedCode(wanted.Wanted).ToString(),
        local.ToString()
      );
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

  private static AssetLocation WantedCode(AssetLocation wanted) =>
    new(wanted.Domain, ExWildcards.FirstAlternative(wanted.Path));

  private static Block Choose(ScenarioContext ctx, AssetLocation wanted) {
    AssetLocation code = WantedCode(wanted);
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
