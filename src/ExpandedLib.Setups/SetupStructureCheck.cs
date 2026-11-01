#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>Checks a drawn machine against the cells its structure occupies (setups.md,
/// Structures).</summary>
internal static class SetupStructureCheck
{
  /// <summary>Whether <paramref name="block"/> at <paramref name="pos"/> is a multiblock whose
  /// layout the rig reads.</summary>
  public static bool IsMultiblock(Block block, BlockEntity? be) =>
    be is BlockEntityMultiblockStructure
    && block.Attributes?.Token is JObject attributes
    && attributes["multiblockStructure"] is JObject;

  /// <summary>The footprint cells a principal at <paramref name="pos"/> takes in
  /// <paramref name="world"/>: a mega-block's reserved cells, else its whole footprint.</summary>
  public static List<FillerCell> Footprint(
    IWorldAccessor world,
    IFillerHost host,
    BlockPos pos,
    int angle
  ) =>
    host is BlockFilledMegastructure mega
      ? mega.ReservedCellsAt(world, pos)
      : StructureFillers.FootprintCells(host, pos, angle);

  /// <summary>The cells the structure at <paramref name="pos"/> occupies besides its principal:
  /// the footprint, and the layout cells that do not want air.</summary>
  public static IReadOnlyList<(int X, int Y, int Z)> Cells(
    IWorldAccessor world,
    Block block,
    BlockPos pos,
    int angle
  )
  {
    var cells = new List<BlockPos>();
    if (block is IFillerHost host)
      cells.AddRange(Footprint(world, host, pos, angle).Select(c => c.Pos));
    BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
    if (IsMultiblock(block, be))
    {
      var anchor = (BlockEntityMultiblockStructure)be!;
      cells.AddRange(
        SetupStructureCells
          .Of(anchor, (JObject)block.Attributes!.Token, anchor.LayoutAngle)
          .Where(c => new AssetLocation(c.Wanted).Path != "air")
          .Select(c => c.Pos)
      );
    }
    return
    [
      .. cells
        .Where(c => !c.Equals(pos))
        .Select(c => (c.X, c.Y, c.Z))
        .Distinct(),
    ];
  }

  /// <summary>The failures of the drawing against the structure placed from
  /// <paramref name="entry"/>: a <c>cells</c> or <c>port</c> glyph outside the structure, and a
  /// structure cell drawn with no glyph.</summary>
  public static IEnumerable<string> Failures(
    SetupLayout layout,
    LayoutEntry entry,
    IReadOnlyList<(int X, int Y, int Z)> structure
  )
  {
    var inside = new HashSet<(int X, int Y, int Z)>(structure);
    if (entry.Id != null)
      foreach (LayoutCell c in layout.Cells)
        if (
          layout.EntryOf(c) is { Target: not null } named
          && named.Target == entry.Id
          && !inside.Contains((c.X, c.Y, c.Z))
        )
          yield return $"glyph '{c.Symbol}' at ({c.X}, {c.Y}, {c.Z}) is outside the cells of \"{entry.Id}\"";
    var drawn = new HashSet<(int X, int Y, int Z)>(
      layout.Cells.Select(c => (c.X, c.Y, c.Z))
    );
    foreach (var cell in structure)
      if (!drawn.Contains(cell))
        yield return $"cell ({cell.X}, {cell.Y}, {cell.Z}) of the structure is drawn with no glyph";
  }
}
#endif
