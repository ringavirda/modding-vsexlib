using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>run</c>: the scenario player places the straight segment by hand in every cell of a
/// straight path, the pipes orient themselves along it, and every <c>open</c> face is then a connector of
/// the block that went down. A path with a turn is refused, since one blocktype is never a bend.</summary>
internal sealed class RunStep : ScenarioStep {
  public override string Kind => "run";

  public override string What(SetupStep step) =>
    step.Id ?? (string)step.Body["code"]!;

  /// <summary>The cells from <paramref name="from"/> through each of <paramref name="via"/> to
  /// <paramref name="to"/>, in order, with the cell two legs share listed once.</summary>
  /// <exception cref="StepFailure">A leg runs along more than one axis.</exception>
  public static List<BlockPos> Path(
    BlockPos from,
    IReadOnlyList<BlockPos> via,
    BlockPos to
  ) {
    List<BlockPos> corners = [from, .. via, to];
    List<BlockPos> cells = [from.Copy()];
    for (int i = 1; i < corners.Count; i++) {
      BlockPos a = corners[i - 1];
      BlockPos b = corners[i];
      int moving =
        (a.X != b.X ? 1 : 0) + (a.Y != b.Y ? 1 : 0) + (a.Z != b.Z ? 1 : 0);
      if (moving > 1)
        throw new StepFailure(
          $"the leg from {FailureDetail.Cell(a)} to {FailureDetail.Cell(b)} runs along more than one axis.",
          Suspect.Scenario
        );
      var step = new Vec3i(
        Math.Sign(b.X - a.X),
        Math.Sign(b.Y - a.Y),
        Math.Sign(b.Z - a.Z)
      );
      for (BlockPos at = a.Copy(); !at.Equals(b);) {
        at = at.AddCopy(step.X, step.Y, step.Z);
        cells.Add(at);
      }
    }
    return cells;
  }

  /// <summary>The first cell of <paramref name="cells"/> where the path changes direction, or null
  /// when it runs straight.</summary>
  public static BlockPos? FirstTurn(IReadOnlyList<BlockPos> cells) {
    for (int i = 1; i + 1 < cells.Count; i++)
      if (
        cells[i - 1].X - cells[i].X != cells[i].X - cells[i + 1].X
        || cells[i - 1].Y - cells[i].Y != cells[i].Y - cells[i + 1].Y
        || cells[i - 1].Z - cells[i].Z != cells[i].Z - cells[i + 1].Z
      )
        return cells[i];
    return null;
  }

  /// <summary>The cells just past each end of a path of at least two cells, along its first and last
  /// legs.</summary>
  public static (BlockPos Before, BlockPos After) Beyond(
    IReadOnlyList<BlockPos> cells
  ) {
    BlockPos a = cells[0];
    BlockPos b = cells[1];
    BlockPos y = cells[^2];
    BlockPos z = cells[^1];
    return (
      a.AddCopy(a.X - b.X, a.Y - b.Y, a.Z - b.Z),
      z.AddCopy(z.X - y.X, z.Y - y.Y, z.Z - y.Z)
    );
  }

  /// <summary>The face the player clicks to place each of <paramref name="cells"/>: the face of the
  /// cell before it that looks toward it, and for the first cell the face of the cell after it that
  /// looks back, so a pipe prefers the side its neighbour is on. A single cell is clicked from
  /// below.</summary>
  public static List<BlockFacing> ClickFaces(IReadOnlyList<BlockPos> cells) {
    if (cells.Count == 1)
      return [BlockFacing.UP];
    List<BlockFacing> faces = [Toward(cells[1], cells[0])];
    for (int i = 1; i < cells.Count; i++)
      faces.Add(Toward(cells[i - 1], cells[i]));
    return faces;
  }

  /// <summary>The face of <paramref name="from"/> that looks toward the adjacent cell
  /// <paramref name="to"/>.</summary>
  public static BlockFacing Toward(BlockPos from, BlockPos to) =>
    BlockFacing.ALLFACES.Single(f =>
      f.Normali.X == Math.Sign(to.X - from.X)
      && f.Normali.Y == Math.Sign(to.Y - from.Y)
      && f.Normali.Z == Math.Sign(to.Z - from.Z)
    );

  /// <summary>Why <paramref name="face"/> is not an open connector of the block at
  /// <paramref name="cell"/>, or null when it is.</summary>
  /// <param name="connects">Whether the placed block has a connector on the face.</param>
  public static string? OpenFailure(
    BlockPos cell,
    BlockFacing face,
    string code,
    System.Func<BlockFacing, bool> connects
  ) =>
    connects(face)
      ? null
      : $"face {face.Code} of {FailureDetail.Cell(cell)} is not a connector of {code}.";

  /// <summary>Places a block by the scenario player at an absolute cell, the click landing on
  /// <paramref name="face"/> of a neighbour.</summary>
  /// <param name="failure">Null when the block went down; otherwise the code the player gave.</param>
  /// <returns>Whether the block went down.</returns>
  public delegate bool PlayerPlace(
    Block block,
    BlockPos at,
    BlockFacing face,
    out string? failure
  );

  /// <summary>Turns the block at an absolute cell one step with the wrench.</summary>
  /// <param name="at">The absolute cell of the block.</param>
  /// <param name="dir">1 for the next orientation of the block's cycle, -1 for the one before.</param>
  /// <returns>Whether the block answers a wrench.</returns>
  public delegate bool PlayerWrench(BlockPos at, int dir);

  /// <summary>Lays a straight path by the player: <paramref name="blocks"/> in the order of
  /// <paramref name="cells"/>, each placed on the click that its neighbour in the path gives it.</summary>
  /// <exception cref="StepFailure">The path turns, a cell holds a block other than air at or above
  /// y 0, or the player cannot place a block.</exception>
  public static void Lay(
    ScenarioContext ctx,
    PlayerPlace place,
    IReadOnlyList<Block> blocks,
    IReadOnlyList<BlockPos> cells
  ) {
    if (FirstTurn(cells) is { } turn)
      throw new StepFailure(
        $"the path turns at {FailureDetail.Cell(turn)}, and a run places one straight blocktype, which is never a bend.",
        Suspect.Scenario
      );
    LayCells(ctx, place, blocks, cells, ClickFaces(cells));
  }

  /// <summary>Lays <paramref name="blocks"/> in the order of <paramref name="cells"/> with no check on the
  /// path's shape, each placed on the click that its entry of <paramref name="faces"/> gives it. The
  /// block that goes down is the one the block's own placement picks, which need not be the one
  /// given. A cell below y 0 is not checked: it is ground, which the caller has dug when it must be
  /// clear.</summary>
  /// <param name="placed">Called with the index of each cell right after its block went down, before the
  /// next is placed; null for none.</param>
  /// <exception cref="StepFailure">A cell holds a block other than air at or above y 0, a cell
  /// lies outside the world, the player cannot place a block, or <paramref name="placed"/> throws.</exception>
  public static void LayCells(
    ScenarioContext ctx,
    PlayerPlace place,
    IReadOnlyList<Block> blocks,
    IReadOnlyList<BlockPos> cells,
    IReadOnlyList<BlockFacing> faces,
    Action<int>? placed = null
  ) {
    foreach (BlockPos cell in cells)
      if (cell.Y >= 0 && !ctx.IsAir(cell))
        throw new StepFailure(
          $"{FailureDetail.Cell(cell)} holds {ctx.Setup.BlockAt(cell).Code}, not air.",
          Suspect.Scenario
        );
    for (int i = 0; i < cells.Count; i++) {
      BlockPos cell = cells[i];
      Block block = blocks[i];
      PackLedger.DrawBlock(ctx, block.Code.ToString(), cell.ToString());
      ctx.RequireInWorld(cell);
      if (
        !place(block, ctx.Setup.Absolute(cell), faces[i], out string? failure)
      )
        throw new StepFailure(
          $"the player could not place {block.Code} at {FailureDetail.Cell(cell)}: {failure}.",
          Suspect.Mod
        );
      ctx.Placed.Add(cell.Copy());
      ctx.Touched.Add(cell.Copy());
      placed?.Invoke(i);
    }
  }

  /// <summary>Fails when two network cells of a placed run are not in one network, the cells just past
  /// each end of the run counted in.</summary>
  /// <exception cref="StepFailure">Two of the cells are in different networks.</exception>
  public static void CheckJoined(
    ScenarioContext ctx,
    IReadOnlyList<BlockPos> cells,
    Block block
  ) {
    if (block is INetworkMember { NetworkType: var type }) {
      List<BlockPos> joined = [.. cells];
      if (cells.Count > 1) {
        (BlockPos before, BlockPos after) = Beyond(cells);
        foreach (BlockPos outside in new[] { before, after })
          if (
            ctx.Setup.BlockAt(outside)
              is INetworkMember { NetworkType: var other }
            && other == type
          )
            joined.Add(outside);
      }
      for (int i = 1; i < joined.Count; i++)
        if (
          ctx.Setup.NetworkAt(joined[0], type) is not { } first
          || !ReferenceEquals(first, ctx.Setup.NetworkAt(joined[i], type))
        )
          throw new StepFailure(
            $"{FailureDetail.Cell(joined[0])} and {FailureDetail.Cell(joined[i])} are not in one {type} network."
          );
    }
  }

  /// <summary>Fails when <paramref name="face"/> of the placed block at the plot-local
  /// <paramref name="cell"/> is not one of its connectors.</summary>
  /// <exception cref="StepFailure">The face is not a connector of the block there.</exception>
  public static void CheckFace(
    ScenarioContext ctx,
    BlockPos cell,
    BlockFacing face
  ) {
    Block placed = ctx.Setup.BlockAt(cell);
    BlockPos at = ctx.Setup.Absolute(cell);
    string? failure = OpenFailure(
      cell,
      face,
      placed.Code.ToString(),
      f =>
        placed is INetworkMember member
        && member.HasConnectorAt(ctx.World.BlockAccessor, at, f)
    );
    if (failure != null)
      throw new StepFailure(failure);
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"run\" needs the scenario player.",
        Suspect.Scenario
      );
    Block block = ctx.BlockOf((string)o["code"]!);
    BlockPos from = ctx.Position(step, o["from"], "from");
    BlockPos to = ctx.Position(step, o["to"], "to");
    List<BlockPos> via =
    [
      .. (o["via"] as JArray ?? []).Select(v => ctx.Position(step, v, "via")),
    ];
    List<BlockPos> cells = Path(from, via, to);
    Lay(ctx, player.Place, [.. cells.Select(_ => block)], cells);
    yield return true;
    foreach (JToken each in o["open"] as JArray ?? [])
      CheckOpen(ctx, step, (JObject)each);
    CheckJoined(ctx, cells, block);
    if (step.Id != null)
      ctx.Setup.Place(step.Id, new SetupPlacement(cells[0].Copy(), cells));
    report.Value = $"{cells.Count} cell(s) of {block.Code}";
  }

  private static void CheckOpen(
    ScenarioContext ctx,
    SetupStep step,
    JObject open
  ) {
    BlockPos cell = ctx.Position(step, open["at"], "open.at");
    string token = (string)open["face"]!;
    BlockFacing face =
      ExOrientation.FacingFromSide(token)
      ?? throw new StepFailure($"\"{token}\" is not a face.", Suspect.Scenario);
    CheckFace(ctx, cell, face);
  }
}
