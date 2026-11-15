using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>Where a held part would go for a given aim: the cell, the face it leans on, and whether that face is a joint.</summary>
/// <param name="Cell">The cell the part would fill.</param>
/// <param name="Anchor">The face of <paramref name="Cell"/> toward the aimed block.</param>
/// <param name="Joinable">True when the block across <paramref name="Anchor"/> offers an open crossing the part mates; false when the anchor is only a support (ground, wall, a pipe's side).</param>
public readonly record struct HandAssemblyTarget(
  BlockPos Cell,
  BlockFacing Anchor,
  bool Joinable
) {
  /// <summary>Resolves the target of an aim.</summary>
  /// <param name="world">The world read for the aimed block and the anchor's neighbour.</param>
  /// <param name="selection">The block the player aims at: position, face and hit point.</param>
  /// <param name="part">The held block.</param>
  public static HandAssemblyTarget Resolve(
    IBlockAccessor world,
    BlockSelection selection,
    Block part
  ) {
    BlockPos aimed = selection.Position;
    BlockPos cell = world.GetBlock(aimed).IsReplacableBy(part)
      ? aimed.Copy()
      : aimed.AddCopy(selection.Face);
    BlockFacing anchor = selection.Face.Opposite;

    return new HandAssemblyTarget(
      cell,
      anchor,
      HandAssemblyCandidates.OffersCrossing(world, cell, anchor, part)
    );
  }
}
