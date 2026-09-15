using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>Finds which network a cell belongs to; membership is selected by network type, not CLR type.</summary>
public static class NetworkMembership {
  /// <summary>Every membership on <paramref name="be"/>; empty when it is on no network.</summary>
  public static IEnumerable<BEBehaviorNetworkMember> MembersOf(
    BlockEntity? be
  ) => be?.Behaviors.OfType<BEBehaviorNetworkMember>() ?? [];

  /// <summary>The membership declaring <paramref name="networkType"/> on <paramref name="be"/>, or null. A block entity carries at most one membership per network type.</summary>
  public static BEBehaviorNetworkMember? MemberOf(
    BlockEntity? be,
    string networkType
  ) =>
    MembersOf(be)
      .FirstOrDefault(m =>
        string.Equals(m.NetworkType, networkType, StringComparison.Ordinal)
      );

  /// <summary>How the cell at <paramref name="pos"/> participates in <paramref name="networkType"/>, or null. A membership behaviour answers first, then the block.</summary>
  public static INetworkMember? Resolve(
    IBlockAccessor world,
    BlockPos pos,
    string networkType
  ) {
    foreach (
      BEBehaviorNetworkMember member in MembersOf(world.GetBlockEntity(pos))
    )
      if (JoinsAt(member, world, pos, networkType))
        return member;

    return
      world.GetBlock(pos) is INetworkConnector connector
      && JoinsAt(connector, world, pos, networkType)
      ? connector
      : null;
  }

  /// <summary>Whether the cell at <paramref name="pos"/> exposes a network connector on <paramref name="face"/>, network-type agnostic.</summary>
  public static bool CouplesAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) =>
    MembersOf(world.GetBlockEntity(pos))
      .Any(m => m.HasConnectorAt(world, pos, face))
    || (
      world.GetBlock(pos) is INetworkConnector connector
      && connector.HasConnectorAt(world, pos, face)
    );

  /// <summary>Whether <paramref name="member"/> reports <paramref name="networkType"/> at <paramref name="pos"/>.</summary>
  private static bool JoinsAt(
    INetworkMember member,
    IBlockAccessor world,
    BlockPos pos,
    string networkType
  ) =>
    string.Equals(
      member.NetworkTypeAt(world, pos),
      networkType,
      StringComparison.Ordinal
    );
}
