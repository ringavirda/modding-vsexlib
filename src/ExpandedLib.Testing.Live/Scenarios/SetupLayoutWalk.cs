using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
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
/// against its drawing. A run cell of a network block is laid as the member of its family whose
/// faces are the cell's drawn faces, and one the block's own placement orients otherwise is turned
/// to them with the wrench; any other run cell is laid as its entry's code. Prints one line per
/// placing entry, labelled <c>L</c> and its place among them.</summary>
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
  /// <param name="wrench">Turns a placed network block by the scenario player's wrench; null for none.</param>
  /// <exception cref="StepFailure">An entry does not place, a structure disagrees with its drawing, a
  /// run is drawn and <paramref name="place"/> is null, a run cell's drawn faces are no member of its
  /// family (suspect scenario), a run cell stays off its drawn faces through every orientation the
  /// wrench offers, or the cells of a run are not in one network (suspect mod). The entry's FAIL line
  /// is printed first.</exception>
  public static IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupLayout layout,
    RunStep.PlayerPlace? place,
    RunStep.PlayerWrench? wrench = null
  ) {
    int placed = 0;
    foreach (LayoutPlacing p in Plan(layout)) {
      string label = "L" + ++placed;
      ctx.Current = label;
      string what = Describe(p.Entry);
      var report = new StepReport();
      using IEnumerator<bool> running = Place(
          ctx,
          layout,
          p,
          place,
          wrench,
          report
        )
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
    RunStep.PlayerWrench? wrench,
    StepReport report
  ) =>
    p.Run is { } run
      ? PlaceRun(ctx, layout, run, place, wrench, report)
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
    SetupLayout layout,
    LayoutRun run,
    RunStep.PlayerPlace? place,
    RunStep.PlayerWrench? wrench,
    StepReport report
  ) {
    if (place == null)
      throw new StepFailure(
        "\"run\" needs the scenario player.",
        Suspect.Scenario
      );
    List<BlockPos> laid =
    [
      .. run.Cells.Select(c => new BlockPos(c.Cell.X, c.Cell.Y, c.Cell.Z)),
    ];
    var wanted = new string?[run.Cells.Count];
    var blocks = new List<Block>();
    for (int i = 0; i < run.Cells.Count; i++) {
      LayoutRunCell cell = run.Cells[i];
      Block entry = ctx.BlockOf(cell.Entry.Code!);
      if (entry is not INetworkConnector connector) {
        blocks.Add(entry);
        continue;
      }
      try {
        IReadOnlyList<string> wildcards = SetupRunFamily.Wildcards(
          ctx.World,
          cell.Entry.Family,
          cell.Entry.Code!
        );
        HashSet<BlockFacing> faces = SetupRunFamily.DrawnFaces(
          layout,
          cell,
          connector.NetworkType,
          ctx.BlockOf,
          ctx.World.BlockAccessor,
          at => ctx.Setup.Absolute(at)
        );
        blocks.Add(
          SetupRunFamily.Choose(
            SetupRunFamily.Members(ctx.World, wildcards),
            faces,
            FailureDetail.Cell(laid[i])
          )
        );
        wanted[i] = SetupRunFamily.Letters(faces);
      } catch (InvalidOperationException e) {
        throw new StepFailure(e.Message, Suspect.Scenario);
      }
    }
    RunStep.LayCells(
      ctx,
      place,
      blocks,
      laid,
      ClickFaces(run),
      i => {
        if (wanted[i] is { } faces)
          Turn(ctx, wrench, laid[i], faces);
      }
    );
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

  /// <summary>Turns the network block just placed at <paramref name="cell"/> with the wrench until
  /// its connectors are <paramref name="wanted"/>, stopping when an orientation comes round
  /// again.</summary>
  private static void Turn(
    ScenarioContext ctx,
    RunStep.PlayerWrench? wrench,
    BlockPos cell,
    string wanted
  ) {
    string Faces() =>
      ctx.Setup.BlockAt(cell) is INetworkConnector held
        ? SetupRunFamily.FacesOf(held)
        : "";
    List<string> offered = [ctx.Setup.BlockAt(cell).Code.ToString()];
    while (Faces() != wanted) {
      if (wrench == null || !wrench(ctx.Setup.Absolute(cell), 1))
        throw new StepFailure(
          $"{offered[^1]} at {FailureDetail.Cell(cell)} holds the faces {Faces()}, "
            + $"the drawing has {wanted}, and it cannot be turned with a wrench.",
          Suspect.Mod
        );
      string now = ctx.Setup.BlockAt(cell).Code.ToString();
      if (offered.Contains(now))
        throw new StepFailure(
          $"the wrench never turned {offered[0]} at {FailureDetail.Cell(cell)} to "
            + $"the drawn faces {wanted}; it offered {string.Join(", ", offered)}.",
          Suspect.Mod
        );
      offered.Add(now);
    }
  }

  /// <summary>The click face of each cell of <paramref name="run"/>: the face of the cell it was
  /// reached from in the depth-first path that looks toward it, and for the first cell the face of
  /// its first neighbour that looks back. A single cell is clicked from below.</summary>
  private static List<BlockFacing> ClickFaces(LayoutRun run) {
    var at = run.Cells.ToDictionary(c => (c.Cell.X, c.Cell.Y, c.Cell.Z));
    var parent = new Dictionary<(int, int, int), BlockPos>();
    var seen = new HashSet<(int, int, int)>();
    void Visit(LayoutRunCell cell) {
      var key = (cell.Cell.X, cell.Cell.Y, cell.Cell.Z);
      seen.Add(key);
      foreach (BlockFacing f in cell.Joins) {
        var next = (
          key.Item1 + f.Normali.X,
          key.Item2 + f.Normali.Y,
          key.Item3 + f.Normali.Z
        );
        if (at.TryGetValue(next, out LayoutRunCell? to) && !seen.Contains(next)) {
          parent[next] = new BlockPos(key.Item1, key.Item2, key.Item3);
          Visit(to);
        }
      }
    }
    Visit(run.Cells[0]);
    var faces = new List<BlockFacing>();
    for (int i = 0; i < run.Cells.Count; i++) {
      var cell = new BlockPos(
        run.Cells[i].Cell.X,
        run.Cells[i].Cell.Y,
        run.Cells[i].Cell.Z
      );
      if (i > 0)
        faces.Add(RunStep.Toward(parent[(cell.X, cell.Y, cell.Z)], cell));
      else if (run.Cells.Count == 1)
        faces.Add(BlockFacing.UP);
      else
        faces.Add(
          RunStep.Toward(
            new BlockPos(
              run.Cells[1].Cell.X,
              run.Cells[1].Cell.Y,
              run.Cells[1].Cell.Z
            ),
            cell
          )
        );
    }
    return faces;
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
