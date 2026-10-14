#if GAME_GE_1_22
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>Aims a click at a control's selection box, for the harness to send as the player's selection.</summary>
internal static class ControlAim
{
  /// <summary>The selection a click on control <paramref name="controlId"/> of the cell at
  /// <paramref name="cell"/> carries: its box's index and id among the cell's selection boxes, and the hit at
  /// the centre of the box's <paramref name="face"/> side.</summary>
  /// <param name="accessor">The accessor the cell's block and its boxes are read through.</param>
  /// <param name="cell">The cell clicked.</param>
  /// <param name="controlId">The id the control's box carries.</param>
  /// <param name="face">The side of the box the hit lands on; null for the top.</param>
  /// <exception cref="ArgumentException"><paramref name="controlId"/> is no box's id of the cell; the message
  /// names the ids the cell has, or says there are none.</exception>
  public static BlockSelection Selection(
    IBlockAccessor accessor,
    BlockPos cell,
    string controlId,
    BlockFacing? face
  )
  {
    Cuboidf[] boxes = accessor.GetBlock(cell).GetSelectionBoxes(accessor, cell);
    int index = Array.FindIndex(
      boxes,
      b => b is CuboidfWithId { Id: var id } && id == controlId
    );
    if (index < 0)
    {
      string[] ids = boxes
        .OfType<CuboidfWithId>()
        .Select(b => b.Id)
        .OfType<string>()
        .ToArray();
      throw new ArgumentException(
        $"{cell} has no control \"{controlId}\"; its controls are "
          + (ids.Length == 0 ? "none" : string.Join(", ", ids))
          + ".",
        nameof(controlId)
      );
    }
    BlockFacing side = face ?? BlockFacing.UP;
    Cuboidf box = boxes[index];
    return new BlockSelection
    {
      Position = cell.Copy(),
      Face = side,
      HitPosition = new Vec3d(
        Side(side.Normali.X, box.X1, box.X2),
        Side(side.Normali.Y, box.Y1, box.Y2),
        Side(side.Normali.Z, box.Z1, box.Z2)
      ),
      SelectionBoxIndex = index,
      SelectionBoxId = controlId,
    };
  }

  private static double Side(int normal, float low, float high) =>
    normal > 0 ? high
    : normal < 0 ? low
    : (low + high) / 2.0;
}
#endif
