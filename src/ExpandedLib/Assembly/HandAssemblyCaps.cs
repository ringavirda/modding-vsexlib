using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The ends of a fixed piece a cap goes on or comes off: which arm a hit aims at, and why an end takes no cap. Reads only.</summary>
internal static class HandAssemblyCaps {
  /// <summary>The arm of <paramref name="node"/> at <paramref name="cell"/> whose face centre is nearest <paramref name="hit"/>.</summary>
  /// <param name="accessor">Reads the piece's arms.</param>
  /// <param name="node">The piece's block.</param>
  /// <param name="cell">The piece's position.</param>
  /// <param name="hit">The hit point inside the cell, each axis in 0 to 1.</param>
  /// <param name="among">Keeps the arms to choose from; null keeps every arm.</param>
  /// <returns>Null when no arm is kept.</returns>
  internal static BlockFacing? NearestArm(
    IBlockAccessor accessor,
    BlockNetworkNode node,
    BlockPos cell,
    Vec3d hit,
    System.Func<BlockFacing, bool>? among = null
  ) =>
    BlockFacing
      .ALLFACES.Where(face =>
        node.ShapeConnectorAt(accessor, cell, face)
        && (among == null || among(face))
      )
      .OrderBy(face =>
        new Vec3d(
          0.5 + 0.5 * face.Normali.X,
          0.5 + 0.5 * face.Normali.Y,
          0.5 + 0.5 * face.Normali.Z
        ).SquareDistanceTo(hit)
      )
      .FirstOrDefault();

  /// <summary>Why the end <paramref name="face"/> of a fixed piece takes no cap: it carries one already, it is joined to the piece beside it, or the block beside it closes it.</summary>
  /// <param name="world">The world read; its network system names the joined faces.</param>
  /// <param name="node">The piece's block.</param>
  /// <param name="cell">The piece's position.</param>
  /// <param name="record">The piece's assembly record.</param>
  /// <param name="face">An arm of the piece.</param>
  /// <returns>The refusal code; null when the end takes a cap.</returns>
  internal static string? FitRefusal(
    IWorldAccessor world,
    BlockNetworkNode node,
    BlockPos cell,
    BEBehaviorAssembled record,
    BlockFacing face
  ) =>
    record.CapAt(face) != null
      ? HandAssemblyServer.Occupied
      : OpenRefusal(world, node, cell, face);

  /// <summary>Why the uncapped end <paramref name="face"/> takes no cap: <see cref="HandAssemblyServer.CapJoined"/> when it is joined to the piece beside it, <see cref="HandAssemblyServer.CapClosed"/> when the block beside it closes it.</summary>
  /// <returns>Null when the end is open.</returns>
  internal static string? OpenRefusal(
    IWorldAccessor world,
    BlockNetworkNode node,
    BlockPos cell,
    BlockFacing face
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    INetworkMember member =
      NetworkMembership.Resolve(
        accessor,
        cell,
        ((INetworkMember)node).NetworkTypeAt(accessor, cell)
      ) ?? node;
    if (
      !world
        .Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
        .GetOpenConnectorFaces(accessor, cell, member)
        .Contains(face)
    )
      return HandAssemblyServer.CapJoined;
    return NetworkFaces.ClosesOpening(
      accessor,
      cell.AddCopy(face),
      face.Opposite
    )
      ? HandAssemblyServer.CapClosed
      : null;
  }
}
