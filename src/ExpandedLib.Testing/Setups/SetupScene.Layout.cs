#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Definitions;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private readonly Dictionary<
    string,
    IReadOnlyList<(Block Block, string Faces)>
  > _family = [];

  private readonly Dictionary<BlockPos, RunCell> _runCells = [];

  private sealed record RunCell(
    IReadOnlyList<(Block Block, string Faces)> Family,
    HashSet<BlockFacing> Faces
  );

  private void PlaceLayout()
  {
    if (Model.Drawn is not { } layout)
      return;
    foreach (LayoutEntry entry in layout.Entries)
    {
      switch (entry.Kind)
      {
        case LayoutEntryKind.Block:
          foreach (
            LayoutCell cell in layout.Cells.Where(c => c.Symbol == entry.Glyph)
          )
            PlaceDrawn(layout, entry, cell);
          break;
        case LayoutEntryKind.Run:
          if (layout.Runs.FirstOrDefault(r => r.Entries[0] == entry) is { } run)
          {
            PlaceRun(layout, run);
            if (run.Id != entry.Id)
              RunAfterEntry(run.Id);
          }
          break;
      }
      if (entry.Id != null)
        RunAfterEntry(entry.Id);
    }
  }

  private void RunAfterEntry(string id)
  {
    foreach (var (target, steps) in Model.AfterEntry)
      if (target == id)
        RunSteps(steps);
  }

  private void PlaceDrawn(
    SetupLayout layout,
    LayoutEntry entry,
    LayoutCell cell
  )
  {
    string where = $"legend \"{entry.Glyph}\"";
    Block block = Resolve(entry.Code!, where);
    var pos = new BlockPos(cell.X, cell.Y, cell.Z);
    Put(block, pos, where);
    if (entry.Id != null)
      Record(entry.Id, pos, block);
    CheckStructure(layout, entry, block, pos, where);
    if ((string?)entry.Body["complete"] == "all")
      Complete(pos, where);
  }

  private void CheckStructure(
    SetupLayout layout,
    LayoutEntry entry,
    Block block,
    BlockPos pos,
    string where
  )
  {
    BlockEntity? be = World.GetBlockEntity(pos);
    if (
      block is not IFillerHost
      && !SetupStructureCheck.IsMultiblock(block, be)
    )
      return;
    int angle = FrameOf(block, pos)?.Angle ?? 0;
    string[] failures =
    [
      .. SetupStructureCheck.Failures(
        layout,
        entry,
        SetupStructureCheck.Cells(World.World, block, pos, angle)
      ),
    ];
    if (failures.Length > 0)
      throw Fail(
        where,
        $"{block.Code} at {Describe(pos)} disagrees with its drawing: {string.Join("; ", failures)}."
      );
  }

  private void PlaceRun(SetupLayout layout, LayoutRun run)
  {
    var laid = new List<BlockPos>();
    foreach (LayoutRunCell cell in run.Cells)
    {
      string where = $"run \"{run.Id}\" legend \"{cell.Entry.Glyph}\"";
      IReadOnlyList<(Block Block, string Faces)> family = Family(
        cell.Entry.Family,
        cell.Entry.Code!,
        where
      );
      HashSet<BlockFacing> faces = SetupRunFamily.DrawnFaces(
        layout,
        cell,
        ((INetworkConnector)family[0].Block).NetworkType,
        code => Resolve(code, where),
        World.Accessor,
        at => at
      );
      var pos = new BlockPos(cell.Cell.X, cell.Cell.Y, cell.Cell.Z);
      Block block = LayCell(family, faces, pos, where, cell.Entry.Code);
      laid.Add(pos);
      if (cell.Entry.Id != null)
        Record(cell.Entry.Id, pos, block);
    }
    _placed[run.Id] = new SetupPlacement(laid[0], laid);
  }

  private Block LayCell(
    IReadOnlyList<(Block Block, string Faces)> family,
    HashSet<BlockFacing> faces,
    BlockPos pos,
    string where,
    string? kind = null
  )
  {
    Block chosen;
    try
    {
      chosen = SetupRunFamily.Choose(family, faces, Describe(pos));
    }
    catch (InvalidOperationException e)
    {
      throw Fail(where, e.Message);
    }
    Put(chosen, pos, where, kind, kind != null);
    _runCells[pos] = new RunCell(family, faces);
    return chosen;
  }

  private void RunStep(SetupStep step)
  {
    JToken body = step.Body;
    var at = new SetupJson.Where(_file.Name, step.Path);
    string code = (string)body["code"]!;
    IReadOnlyList<(Block Block, string Faces)> family = Family(
      SetupLayout.RunFamily(code, body["family"], at),
      code,
      step.Path
    );
    List<BlockPos> corners = [At(step, "from")];
    if (body["via"] is JArray via)
      for (int i = 0; i < via.Count; i++)
        corners.Add(
          SetupPosition.Parse(via[i], at.In("via").At(i)).Resolve(View)
        );
    corners.Add(At(step, "to"));
    var path = new List<BlockPos> { corners[0] };
    var faces = new Dictionary<BlockPos, HashSet<BlockFacing>>();
    Join(faces, corners[0]);
    for (int i = 1; i < corners.Count; i++)
    {
      BlockPos from = corners[i - 1];
      BlockPos to = corners[i];
      int[] delta = [to.X - from.X, to.Y - from.Y, to.Z - from.Z];
      if (delta.Count(d => d != 0) > 1)
        throw Fail(
          step.Path,
          $"the leg from {Describe(from)} to {Describe(to)} does not run along one axis."
        );
      BlockFacing? toward = BlockFacing.ALLFACES.FirstOrDefault(f =>
        Math.Sign(delta[0]) == f.Normali.X
        && Math.Sign(delta[1]) == f.Normali.Y
        && Math.Sign(delta[2]) == f.Normali.Z
      );
      if (toward == null)
        continue;
      for (BlockPos cell = from; !cell.Equals(to); )
      {
        BlockPos next = cell.AddCopy(toward);
        Join(faces, cell).Add(toward);
        Join(faces, next).Add(toward.Opposite);
        path.Add(next);
        cell = next;
      }
    }
    if (body["open"] is JArray opens)
      for (int i = 0; i < opens.Count; i++)
      {
        SetupJson.Where w = at.In("open").At(i);
        BlockPos cell = SetupPosition
          .Parse(opens[i]["at"], w.In("at"))
          .Resolve(View);
        string letter = (string)opens[i]["face"]!;
        BlockFacing face =
          SetupLayout.FaceOf(letter)
          ?? throw Fail(
            step.Path,
            $"open face \"{letter}\" is not one of n, e, s, w, u, d."
          );
        if (!faces.ContainsKey(cell))
          throw Fail(
            step.Path,
            $"open at {Describe(cell)} is no cell of the run."
          );
        faces[cell].Add(face);
        if (_runCells.TryGetValue(cell.AddCopy(face), out RunCell? other))
        {
          other.Faces.Add(face.Opposite);
          World.Accessor.SetBlock(0, cell.AddCopy(face));
          LayCell(other.Family, other.Faces, cell.AddCopy(face), step.Path);
        }
      }
    var laid = new List<BlockPos>();
    foreach (BlockPos cell in path.Distinct())
    {
      LayCell(family, faces[cell], cell, step.Path, code);
      laid.Add(cell);
    }
    if (step.Id != null)
      _placed[step.Id] = new SetupPlacement(laid[0], laid);
  }

  private static HashSet<BlockFacing> Join(
    Dictionary<BlockPos, HashSet<BlockFacing>> faces,
    BlockPos cell
  )
  {
    if (!faces.TryGetValue(cell, out HashSet<BlockFacing>? set))
      faces[cell] = set = [];
    return set;
  }

  private IReadOnlyList<(Block Block, string Faces)> Family(
    IReadOnlyList<string>? declared,
    string code,
    string where
  )
  {
    try
    {
      IReadOnlyList<string> wildcards = SetupRunFamily.Wildcards(
        World.World,
        declared,
        code
      );
      string key = string.Join("|", wildcards);
      if (_family.TryGetValue(key, out var cached))
        return cached;
      return _family[key] = SetupRunFamily.Members(World.World, wildcards);
    }
    catch (InvalidOperationException e)
    {
      throw Fail(where, e.Message);
    }
  }
}
#endif
