#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

/// <summary>Draws the selection outline of a control as a whole: every box that shares the hit box's id
/// across the principal's cell and its fillers, or every body box when the hit has no id. Available on
/// 1.22 only.</summary>
public static class ControlOutline
{
  /// <summary>
  /// The boxes to outline for a hit on <paramref name="hitPos"/>: the boxes with <paramref name="hitId"/>
  /// in the principal's cell and in each filler cell still linked to that principal, or the id-less boxes of
  /// those cells when <paramref name="hitId"/> is null, each translated by its cell's offset from the hit cell.
  /// </summary>
  /// <param name="accessor">The accessor the selection boxes are read through.</param>
  /// <param name="hitPos">The cell the player's selection landed on: the principal or one of its fillers.</param>
  /// <param name="hitId">The selection's box id, null for a body hit.</param>
  /// <returns>Null when the principal's block has no <c>controls</c> table or is gone; the caller then draws
  /// the hit cell's own selection boxes.</returns>
  public static Cuboidf[]? Boxes(
    IBlockAccessor accessor,
    BlockPos hitPos,
    string? hitId
  )
  {
    BlockPos principalPos = accessor.GetBlockEntity(hitPos)
      is BlockEntityStructureFiller { Principal: { } linked }
      ? linked
      : hitPos;
    Block principal = accessor.GetBlock(principalPos);
    if (BlockControls.Of(principal).Count == 0)
      return null;

    var boxes = new List<Cuboidf>();
    AddCell(boxes, accessor, principalPos, hitPos, hitId);
    if (principal is IFillerHost host)
      foreach (
        FillerCell cell in StructureFillers.FootprintCells(
          host,
          principalPos,
          host.StructureAngle
        )
      )
        if (
          !cell.Pos.Equals(principalPos)
          && accessor.GetBlockEntity(cell.Pos)
            is BlockEntityStructureFiller { Principal: { } of }
          && of.Equals(principalPos)
        )
          AddCell(boxes, accessor, cell.Pos, hitPos, hitId);
    return [.. boxes];
  }

  private static void AddCell(
    List<Cuboidf> into,
    IBlockAccessor accessor,
    BlockPos cell,
    BlockPos hitPos,
    string? hitId
  )
  {
    Cuboidf[]? boxes = accessor
      .GetBlock(cell)
      .GetSelectionBoxes(accessor, cell);
    foreach (Cuboidf box in boxes ?? [])
    {
      string? id = (box as CuboidfWithId)?.Id;
      if (id == hitId)
        into.Add(
          box.Clone()
            .Translate(cell.X - hitPos.X, cell.Y - hitPos.Y, cell.Z - hitPos.Z)
        );
    }
  }

  /// <summary>
  /// Draws the outline of <paramref name="sel"/> through <paramref name="render"/> in vanilla's width and in
  /// <paramref name="block"/>'s selection colour: <see cref="Boxes"/> when it answers, else the hit cell's
  /// own selection boxes as vanilla draws them. Draws nothing off the client.
  /// </summary>
  /// <param name="block">The block at the selection, whose <c>RenderSelectionBoxes</c> is calling.</param>
  /// <param name="api">The calling side's api.</param>
  /// <param name="sel">The player's selection; boxes are drawn relative to its position.</param>
  /// <param name="render">The game's box renderer.</param>
  public static void Render(
    Block block,
    ICoreAPI api,
    BlockSelection sel,
    RenderBoxDelegate render
  )
  {
    if (api.Side != EnumAppSide.Client || api is not ICoreClientAPI client)
      return;
    IBlockAccessor accessor = api.World.BlockAccessor;
    BlockPos pos = sel.Position;
    float width = 1.6f * ClientSettings.Wireframethickness;
    Vec4f color = block.GetSelectionColor(client, pos);

    Cuboidf[]? boxes = Boxes(accessor, pos, sel.SelectionBoxId);
    if (boxes != null)
    {
      foreach (Cuboidf box in boxes)
        render(box, width, color);
      return;
    }

    Cuboidf[] own = block.GetSelectionBoxes(accessor, pos);
    bool partial = block.DoPartialSelection(api.World, pos);
    for (int i = 0; i < own.Length; i++)
    {
      if (partial)
        i = sel.SelectionBoxIndex;
      if (own.Length <= i)
        break;
      if (own[i].GetType().Name != "DecorSelectionBox")
        render(own[i], width, color);
      if (partial)
        break;
    }
  }
}
#endif
