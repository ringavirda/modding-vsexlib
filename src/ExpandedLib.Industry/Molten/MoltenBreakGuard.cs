using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// Refuses the ordinary break of a molten holder (a block carrying <see cref="BlockBehaviorMoltenBurn"/>) whose
/// content is liquid. A sneaking or Creative break goes through, and set content always breaks.
/// </summary>
public static class MoltenBreakGuard {
  /// <summary>The in-game error code sent to a player whose break is refused.</summary>
  public const string ErrorCode = "exlib-moltenliquidbreak";

  /// <summary>Subscribes <see cref="OnBreak"/> to the server's break event.</summary>
  /// <param name="api">The server API whose event manager is used.</param>
  public static void Register(ICoreServerAPI api) =>
    api.Event.BreakBlock += OnBreak;

  /// <summary>Whether <paramref name="be"/> holds metal that has not set: in its own
  /// <see cref="IMoltenCell"/>, in a hosted <see cref="BEBehaviorMoltenCell"/>, or as parked content its
  /// <see cref="IMoltenSurface"/> reports liquid. False for null.</summary>
  public static bool HoldsLiquid(BlockEntity? be) =>
    be is IMoltenSurface { SurfaceLiquid: true }
    || be is IMoltenCell { Solidified: false, CellAmount: > 0 }
    || be.MoltenCells().Any(c => !c.Solidified && c.CellAmount > 0);

  /// <summary>Whether the holder at <paramref name="pos"/> holds liquid metal in the block entity there, in
  /// the principal it belongs to when it is a structure filler, or in any cell of that principal's footprint.</summary>
  /// <param name="world">The world the block stands in.</param>
  /// <param name="pos">The broken block's position.</param>
  public static bool HoldsLiquid(IWorldAccessor world, BlockPos pos) =>
    Cells(world, pos)
      .Any(p => HoldsLiquid(world.BlockAccessor.GetBlockEntity(p)));

  /// <summary>Cancels the break of a block carrying <see cref="BlockBehaviorMoltenBurn"/> that
  /// <see cref="HoldsLiquid(IWorldAccessor, BlockPos)"/> and tells the player, unless the player sneaks or is in
  /// Creative mode.</summary>
  /// <param name="byPlayer">The breaking player.</param>
  /// <param name="blockSel">The selected block.</param>
  /// <param name="dropQuantityMultiplier">Unused.</param>
  /// <param name="handling">Set to <see cref="EnumHandling.PreventDefault"/> when the break is refused.</param>
  public static void OnBreak(
    IServerPlayer byPlayer,
    BlockSelection blockSel,
    ref float dropQuantityMultiplier,
    ref EnumHandling handling
  ) {
    IWorldAccessor world = byPlayer.Entity.World;
    if (
      byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative
      || byPlayer.Entity.Controls.Sneak
      || world
        .BlockAccessor.GetBlock(blockSel.Position)
        .GetBehavior<BlockBehaviorMoltenBurn>() == null
      || !HoldsLiquid(world, blockSel.Position)
    )
      return;
    handling = EnumHandling.PreventDefault;
    byPlayer.SendIngameError(ErrorCode);
  }

  private static IEnumerable<BlockPos> Cells(IWorldAccessor world, BlockPos pos) {
    BlockPos principal = world.BlockAccessor.GetBlockEntity(pos)
      is BlockEntityStructureFiller { Principal: { } p }
      ? p
      : pos;
    yield return principal;
    if (world.BlockAccessor.GetBlock(principal) is IFillerHost host)
      foreach (
        FillerCell cell in StructureFillers.FootprintCells(
          host,
          principal,
          host.StructureAngle
        )
      )
        yield return cell.Pos;
  }
}
