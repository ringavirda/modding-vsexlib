using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary>An entry that places something: a block entry with each of its drawn cells in reading
/// order, or a run at its first entry's place with its cells in path order.</summary>
internal sealed record LayoutPlacing(
  LayoutEntry Entry,
  LayoutRun? Run,
  IReadOnlyList<LayoutCell> Cells
);

/// <summary><c>layout</c> read by the live runner: the entries are placed by the world in legend
/// order before the first step, each run laid by the scenario player, and every structure checked
/// against its drawing. Prints one line per placing entry, labelled <c>L</c> and its place among
/// them.</summary>
internal static class SetupLayoutWalk {
  /// <summary>The placing entries of <paramref name="layout"/> in placement order.</summary>
  public static IReadOnlyList<LayoutPlacing> Plan(SetupLayout layout) {
    var plan = new List<LayoutPlacing>();
    foreach (LayoutEntry entry in layout.Entries)
      switch (entry.Kind) {
        case LayoutEntryKind.Block:
          plan.Add(
            new LayoutPlacing(
              entry,
              null,
              [.. layout.Cells.Where(c => c.Symbol == entry.Glyph)]
            )
          );
          break;
        case LayoutEntryKind.Run:
          if (layout.Runs.FirstOrDefault(r => r.Entries[0] == entry) is { } run)
            plan.Add(
              new LayoutPlacing(entry, run, [.. run.Cells.Select(c => c.Cell)])
            );
          break;
      }
    return plan;
  }

  /// <summary>Walks <paramref name="layout"/> in the order of <see cref="Plan"/>.</summary>
  /// <param name="place">Places a run's cells by the scenario player; null when the case has none.</param>
  /// <exception cref="StepFailure">An entry does not place, a structure disagrees with its drawing, or
  /// a run is drawn and <paramref name="place"/> is null. The entry's FAIL line is printed first.</exception>
  public static IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupLayout layout,
    RunStep.PlayerPlace? place
  ) {
    int placed = 0;
    foreach (LayoutPlacing p in Plan(layout)) {
      string label = "L" + ++placed;
      string what = Describe(p.Entry);
      var report = new StepReport();
      using IEnumerator<bool> running = Place(ctx, layout, p, place, report)
        .GetEnumerator();
      while (true) {
        bool more;
        try {
          more = running.MoveNext();
          ctx.ThrowIfLogged();
        } catch (StepFailure e) {
          ctx.Line(ScenarioOutput.StepFail(label, "layout", what, e.Message));
          foreach (string line in FailureDetail.Lines(ctx, null, e))
            ctx.Line(ScenarioOutput.Detail(line));
          e.Reported = true;
          throw;
        }
        if (!more)
          break;
        yield return true;
      }
      ctx.Line(ScenarioOutput.StepOk(label, "layout", what, report.Value));
    }
  }

  private static string Describe(LayoutEntry entry) =>
    $"{entry.Glyph} {entry.Id ?? entry.Run ?? entry.Code}";

  private static IEnumerable<bool> Place(
    ScenarioContext ctx,
    SetupLayout layout,
    LayoutPlacing p,
    RunStep.PlayerPlace? place,
    StepReport report
  ) =>
    p.Run is { } run
      ? PlaceRun(ctx, run, place, report)
      : PlaceBlock(ctx, layout, p.Entry, p.Cells, report);

  private static IEnumerable<bool> PlaceBlock(
    ScenarioContext ctx,
    SetupLayout layout,
    LayoutEntry entry,
    IReadOnlyList<LayoutCell> cells,
    StepReport report
  ) {
    Block block = ctx.BlockOf(entry.Code!);
    int count = 0;
    foreach (LayoutCell cell in cells) {
      var local = new BlockPos(cell.X, cell.Y, cell.Z);
      List<BlockPos> filled = PlaceStep.Put(ctx, block, entry.Code!, local);
      count += filled.Count;
      if (entry.Id != null)
        ctx.Setup.Place(
          entry.Id,
          new SetupPlacement(local.Copy(), filled, ctx.Setup.FrameAt(local))
        );
      CheckStructure(ctx, layout, entry, block, local);
      if ((string?)entry.Body["complete"] == "all")
        foreach (
          bool wait in CompleteStep.Complete(
            ctx,
            local,
            new JValue("all"),
            null,
            new StepReport()
          )
        )
          yield return wait;
    }
    report.Value = $"{count} cell(s) of {block.Code}";
  }

  private static IEnumerable<bool> PlaceRun(
    ScenarioContext ctx,
    LayoutRun run,
    RunStep.PlayerPlace? place,
    StepReport report
  ) {
    if (place == null)
      throw new StepFailure(
        "\"run\" needs the scenario player.",
        Suspect.Scenario
      );
    List<Block> blocks = [.. run.Cells.Select(c => ctx.BlockOf(c.Entry.Code!))];
    List<BlockPos> laid =
    [
      .. run.Cells.Select(c => new BlockPos(c.Cell.X, c.Cell.Y, c.Cell.Z)),
    ];
    RunStep.Lay(ctx, place!, blocks, laid);
    yield return true;
    foreach (LayoutRunCell cell in run.Cells)
      foreach (BlockFacing face in cell.Opens)
        RunStep.CheckFace(
          ctx,
          new BlockPos(cell.Cell.X, cell.Cell.Y, cell.Cell.Z),
          face
        );
    RunStep.CheckJoined(ctx, laid, blocks[0]);
    for (int i = 0; i < laid.Count; i++)
      if (run.Cells[i].Entry.Id is { } id)
        ctx.Setup.Place(id, new SetupPlacement(laid[i], [laid[i]]));
    ctx.Setup.Place(run.Id, new SetupPlacement(laid[0], laid));
    report.Value = $"{laid.Count} cell(s) of {run.Id}";
  }

  private static void CheckStructure(
    ScenarioContext ctx,
    SetupLayout layout,
    LayoutEntry entry,
    Block block,
    BlockPos local
  ) {
    BlockEntity? be = ctx.Setup.BlockEntityAt(local);
    if (
      block is not IFillerHost
      && !SetupStructureCheck.IsMultiblock(block, be)
    )
      return;
    int angle = ctx.Setup.FrameAt(local)?.Angle ?? 0;
    List<(int X, int Y, int Z)> structure =
    [
      .. SetupStructureCheck
        .Cells(ctx.World, block, ctx.Setup.Absolute(local), angle)
        .Select(c =>
        {
          BlockPos l = ctx.Setup.Local(new BlockPos(c.X, c.Y, c.Z));
          return (l.X, l.Y, l.Z);
        }),
    ];
    string[] failures =
    [
      .. SetupStructureCheck.Failures(layout, entry, structure),
    ];
    if (failures.Length > 0)
      throw new StepFailure(
        $"{block.Code} at {FailureDetail.Cell(local)} disagrees with its drawing: {string.Join("; ", failures)}.",
        Suspect.Scenario
      );
  }
}
