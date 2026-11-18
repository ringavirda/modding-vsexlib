using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The server's debug lines of hand assembly: one per fix, take-off, cap fit, cap take-off, break of a fixed piece and refusal.</summary>
internal static class HandAssemblyLog {
  /// <summary>Writes one debug line, <c>[exlib] handassembly {kind} player= mode= piece= cell= joints= fasteners=</c>, to the world's logger.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The player the event is of; null for none, written as <c>-</c>.</param>
  /// <param name="kind">The event: fix, takeoff, capfit, captakeoff, break or refusal.</param>
  /// <param name="piece">The code of the piece; null for none, written as <c>-</c>.</param>
  /// <param name="cell">The piece's position.</param>
  /// <param name="joints">The faces of the piece the event made, took off or lost a joint on; none is written as <c>-</c>.</param>
  /// <param name="given">The stacks the player was given or dropped for, written <c>+n code</c>.</param>
  /// <param name="taken">The stacks the player spent or lost, written <c>-n code</c>.</param>
  /// <param name="refusal">The refusal code of a refusal, appended as <c>code=</c>; null for any other event.</param>
  public static void Write(
    IWorldAccessor world,
    IPlayer? player,
    string kind,
    AssetLocation? piece,
    BlockPos cell,
    IEnumerable<BlockFacing> joints,
    IEnumerable<ItemStack> given,
    IEnumerable<ItemStack> taken,
    string? refusal = null
  ) {
    string faces = string.Join(",", joints.Select(f => f.Code));
    string moved = string.Join(
      ", ",
      Group('+', given).Concat(Group('-', taken))
    );
    world.Api.Logger.Debug(
      "[exlib] handassembly {0} player={1} mode={2} piece={3} cell={4},{5},{6} joints={7} fasteners={8}{9}",
      kind,
      player?.PlayerName ?? "-",
      player?.WorldData.CurrentGameMode.ToString().ToLowerInvariant() ?? "-",
      piece?.ToString() ?? "-",
      cell.X,
      cell.Y,
      cell.Z,
      faces.Length == 0 ? "-" : faces,
      moved.Length == 0 ? "-" : moved,
      refusal == null ? "" : " code=" + refusal
    );
  }

  /// <summary>Writes the refusal line of a refused fix, take-off or cap; nothing moved.</summary>
  public static void Refused(
    IWorldAccessor world,
    IPlayer player,
    AssemblyResult refusal,
    AssetLocation? piece,
    BlockPos cell
  ) => Write(world, player, "refusal", piece, cell, [], [], [], refusal.Code);

  private static IEnumerable<string> Group(
    char sign,
    IEnumerable<ItemStack> stacks
  ) =>
    stacks
      .GroupBy(s => s.Collectible.Code.ToString())
      .Select(g => $"{sign}{g.Sum(s => s.StackSize)} {g.Key}");
}
