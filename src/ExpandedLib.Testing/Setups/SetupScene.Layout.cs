#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Definitions;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private readonly Dictionary<
    char,
    IReadOnlyList<(Block Block, string Faces)>
  > _family = [];

  private void PlaceLayout()
  {
    if (Model.Drawn is not { } layout)
      return;
    foreach (LayoutEntry entry in layout.Entries)
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
            PlaceRun(layout, run);
          break;
      }
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
        SetupStructureCheck.Cells(World, block, pos, angle)
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
    var drawn = layout.Cells.ToDictionary(c => (c.X, c.Y, c.Z));
    var laid = new List<BlockPos>();
    foreach (LayoutRunCell cell in run.Cells)
    {
      string where = $"run \"{run.Id}\" legend \"{cell.Entry.Glyph}\"";
      IReadOnlyList<(Block Block, string Faces)> family = Family(
        cell.Entry,
        where
      );
      string type = ((INetworkConnector)family[0].Block).NetworkType;
      var faces = new HashSet<BlockFacing>(cell.Joins.Concat(cell.Opens));
      foreach (BlockFacing face in BlockFacing.ALLFACES)
      {
        if (faces.Contains(face))
          continue;
        if (
          !drawn.TryGetValue(
            Step((cell.Cell.X, cell.Cell.Y, cell.Cell.Z), face),
            out LayoutCell next
          )
        )
          continue;
        LayoutEntry neighbour = layout.EntryOf(next);
        if (neighbour.Kind != LayoutEntryKind.Block)
          continue;
        if (
          Resolve(neighbour.Code!, where) is INetworkConnector connector
          && connector.NetworkType == type
          && connector.HasConnectorAt(face.Opposite)
        )
          faces.Add(face);
      }
      string wanted = Letters(faces);
      var pos = new BlockPos(cell.Cell.X, cell.Cell.Y, cell.Cell.Z);
      Block[] fits =
      [
        .. family.Where(f => f.Faces == wanted).Select(f => f.Block),
      ];
      if (fits.Length != 1)
        throw Fail(
          where,
          fits.Length == 0
            ? $"cell {Describe(pos)} needs a block with the faces {wanted}, and none of {string.Join(", ", cell.Entry.Family)} has them."
            : $"cell {Describe(pos)} needs a block with the faces {wanted}, and {string.Join(" and ", fits.Select(b => b.Code.ToString()))} both have them."
        );
      Put(fits[0], pos, where);
      laid.Add(pos);
      if (cell.Entry.Id != null)
        Record(cell.Entry.Id, pos, fits[0]);
    }
    _placed[run.Id] = new SetupPlacement(laid[0], laid);
  }

  private IReadOnlyList<(Block Block, string Faces)> Family(
    LayoutEntry entry,
    string where
  )
  {
    if (_family.TryGetValue(entry.Glyph, out var cached))
      return cached;
    (Block Block, string Faces)[] blocks =
    [
      .. World
        .World.Blocks.Where(b =>
          b.Id != 0
          && b is INetworkConnector
          && entry.Family.Any(w => WildcardUtil.Match(w, b.Code.ToString()))
        )
        .Select(b =>
          (
            b,
            Letters(
              BlockFacing.ALLFACES.Where(f =>
                ((INetworkConnector)b).HasConnectorAt(f)
              )
            )
          )
        ),
    ];
    string[] types =
    [
      .. blocks
        .Select(b => ((INetworkConnector)b.Block).NetworkType)
        .Distinct(),
    ];
    if (types.Length != 1)
      throw Fail(
        where,
        types.Length == 0
          ? $"no loaded block takes the family {string.Join(", ", entry.Family)}."
          : $"the family {string.Join(", ", entry.Family)} holds blocks of more than one network type, {string.Join(" and ", types)}."
      );
    return _family[entry.Glyph] = blocks;
  }

  private static string Letters(IEnumerable<BlockFacing> faces) =>
    string.Concat(
      BlockFacing.ALLFACES.Where(faces.Contains).Select(f => f.Code[0])
    );

  private static (int X, int Y, int Z) Step(
    (int X, int Y, int Z) p,
    BlockFacing f
  ) => (p.X + f.Normali.X, p.Y + f.Normali.Y, p.Z + f.Normali.Z);
}
#endif
