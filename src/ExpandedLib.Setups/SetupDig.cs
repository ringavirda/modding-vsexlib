using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary><c>dig</c> (setups.md, Steps the setups add): the cells of an inclusive box in the
/// file's stated ground set to air by the world, with no drop, nothing given to the pack and no
/// tool. Both runners dig through it.</summary>
internal static class SetupDig {
  /// <summary>The setup-local cells of the box from <paramref name="from"/> to
  /// <paramref name="to"/>, every one checked against <paramref name="model"/>'s ground; a null
  /// <paramref name="model"/> states none.</summary>
  /// <exception cref="SetupFileException">The file states no <c>ground</c>, or a cell lies at or
  /// above y 0, below y -<see cref="SetupFileModel.Ground"/> or outside the plot's footprint;
  /// the message names the first such cell.</exception>
  public static List<BlockPos> Cells(
    SetupFileModel? model,
    BlockPos from,
    BlockPos to,
    SetupJson.Where where
  ) {
    if (model is not { Ground: > 0 })
      throw where.Fail("digs only in a file that states \"ground\".");
    var cells = new List<BlockPos>();
    for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
      for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++) {
          string cell = $"({x}, {y}, {z})";
          if (y >= 0)
            throw where.Fail($"{cell} lies at or above y 0, over the ground.");
          if (y < -model.Ground)
            throw where.Fail(
              $"{cell} lies below the ground's {model.Ground} block(s)."
            );
          if (Math.Abs(x) > model.Plot[0] || Math.Abs(z) > model.Plot[2])
            throw where.Fail($"{cell} lies outside the plot's footprint.");
          cells.Add(new BlockPos(x, y, z));
        }
    return cells;
  }

  /// <summary>The setup-local cells below y 0 that <paramref name="layout"/> places a block or the
  /// first entry of a run on, in entry order.</summary>
  public static List<BlockPos> LayoutCells(SetupLayout layout) =>
    [
      .. layout
        .Entries.SelectMany(e =>
          e.Kind == LayoutEntryKind.Block
            ? layout.Cells.Where(c => c.Symbol == e.Glyph)
          : e.Kind == LayoutEntryKind.Run
            ? layout
              .Runs.Where(r => r.Entries[0] == e)
              .SelectMany(r => r.Cells.Select(c => c.Cell))
          : []
        )
        .Where(c => c.Y < 0)
        .Select(c => new BlockPos(c.X, c.Y, c.Z)),
    ];

  /// <summary>Sets every setup-local cell of <paramref name="cells"/> that is not air to air
  /// through <paramref name="world"/>'s block accessor.</summary>
  public static void Dig(ISetupWorld world, IEnumerable<BlockPos> cells) {
    IBlockAccessor blocks = world.World.BlockAccessor;
    foreach (BlockPos cell in cells) {
      BlockPos at = world.Absolute(cell);
      if (blocks.GetBlock(at).Id != 0)
        blocks.SetBlock(0, at);
    }
  }
}
