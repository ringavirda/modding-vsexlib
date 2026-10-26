using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Industry.Molten;

/// <summary>Finds the <see cref="ILiquidMetalSink"/> a pour at a cell lands in.</summary>
public static class MoltenSinks {
  /// <summary>
  /// The liquid metal sink at <paramref name="pos"/>: the block's own, or a structure filler cell's principal's.
  /// </summary>
  /// <param name="world">The world the cell is in.</param>
  /// <param name="pos">The aimed cell.</param>
  /// <param name="offset">Receives the cell's position minus the sink's, so a hit in the cell is a hit offset by it in the sink's cell.</param>
  /// <returns>The sink, or null when neither the cell nor its principal takes a pour.</returns>
  public static ILiquidMetalSink? SinkAt(
    IWorldAccessor world,
    BlockPos pos,
    out Vec3i offset
  ) {
    offset = new Vec3i(0, 0, 0);
    if (
      world
        .BlockAccessor.GetBlock(pos)
        .GetInterface<ILiquidMetalSink>(world, pos) is { } own
    )
      return own;
    if (
      world.BlockAccessor.GetBlockEntity(pos)
      is not BlockEntityStructureFiller { Principal: { } principal }
    )
      return null;
    offset = new Vec3i(
      pos.X - principal.X,
      pos.Y - principal.Y,
      pos.Z - principal.Z
    );
    return world
      .BlockAccessor.GetBlock(principal)
      .GetInterface<ILiquidMetalSink>(world, principal);
  }
}
