using System.Collections.Generic;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// The crowbar interaction of every <see cref="IChiselableMolten"/> holder: a one-second hold breaks out
/// the struck cell and the connected set cells of its metal, up to <see cref="ExlibValues.MoltenBarCells"/>.
/// A holder's block calls <see cref="Start"/>, <see cref="Step"/> and <see cref="Stop"/> from its interact overrides.
/// </summary>
public static class MoltenBar {
  /// <summary>The in-game error code sent to a player who strikes liquid metal with the bar.</summary>
  public const string LiquidError = "exlib-moltenbarliquid";

  /// <summary>Seconds a survival player holds the bar before the break-out happens.</summary>
  public const float HoldSeconds = 1f;

  private const float CreativeHoldSeconds = 0.1f;
  private const float MinStopSeconds = 0.5f;

  /// <summary>True when <paramref name="stack"/> is a crowbar.</summary>
  public static bool IsBar(ItemStack? stack) =>
    MoltenChisel.IsTool(stack, EnumTool.Crowbar);

  /// <summary>True when the player holds a crowbar in the active hand.</summary>
  public static bool IsHeldBy(IPlayer byPlayer) =>
    IsBar(byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack);

  /// <summary>Claims the click when the player holds a crowbar and the holder at the selection has set
  /// content to break out; on the server, a holder with only liquid content sends
  /// <see cref="LiquidError"/> instead and the click falls through.</summary>
  /// <returns>True to start the hold; false when not a crowbar use, the claims refuse it, or nothing can
  /// be broken out.</returns>
  public static bool Start(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (!IsHeldBy(byPlayer) || !CanBuildOrBreak(world, byPlayer, blockSel))
      return false;
    BlockEntity? be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
    if (be is IChiselableMolten { CanBreakOut: true })
      return true;
    if (world.Side == EnumAppSide.Server && MoltenBreakGuard.HoldsLiquid(be))
      (byPlayer as IServerPlayer)?.SendIngameError(LiquidError);
    return false;
  }

  /// <summary>Whether the hold goes on: true until <see cref="HoldSeconds"/> have passed, 0.1 s in
  /// Creative mode.</summary>
  public static bool Step(float secondsUsed, IPlayer byPlayer) =>
    secondsUsed
    < (
      byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative
        ? CreativeHoldSeconds
        : HoldSeconds
    );

  /// <summary>Ends the hold: after more than half a second, or at once in Creative mode, breaks the
  /// stretch out through <see cref="BreakOut"/>.</summary>
  public static void Stop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      !IsHeldBy(byPlayer)
      || !CanBuildOrBreak(world, byPlayer, blockSel)
      || (
        secondsUsed <= MinStopSeconds
        && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative
      )
    )
      return;
    BreakOut(world, byPlayer, blockSel.Position);
  }

  /// <summary>The positions one use breaks out from <paramref name="start"/>: the cell itself, then the
  /// connected cells of the same metal whose <see cref="IChiselableMolten.CanBreakOut"/> holds, nearest
  /// first, at most <paramref name="limit"/> in all. The walk crosses a face only where both cells have a
  /// connector, as the network does, and a cell of another metal or without set content ends it there.
  /// A holder that is no <see cref="IMoltenCell"/> breaks out alone.</summary>
  public static IReadOnlyList<BlockPos> Stretch(
    IBlockAccessor accessor,
    BlockPos start,
    int limit
  ) {
    List<BlockPos> found = [start.Copy()];
    if (accessor.GetBlockEntity(start) is not IMoltenCell origin)
      return found;

    HashSet<BlockPos> seen = [start.Copy()];
    Queue<BlockPos> queue = new();
    queue.Enqueue(start.Copy());
    while (queue.Count > 0 && found.Count < limit) {
      BlockPos cur = queue.Dequeue();
      if (accessor.GetBlock(cur) is not BlockNetworkNode node)
        continue;
      foreach (BlockFacing face in BlockFacing.ALLFACES) {
        BlockPos npos = cur.AddCopy(face);
        if (
          found.Count >= limit
          || !seen.Add(npos)
          || !MoltenNetwork.JointOpen(accessor, cur, node, face, npos)
          || accessor.GetBlockEntity(npos)
            is not (
              IMoltenCell cell
              and IChiselableMolten { CanBreakOut: true }
            )
          || cell.CellMetalType != origin.CellMetalType
        )
          continue;
        found.Add(npos);
        queue.Enqueue(npos);
      }
    }
    return found;
  }

  /// <summary>Breaks out the <see cref="Stretch"/> from <paramref name="pos"/> on the server: each
  /// holder's <see cref="IChiselableMolten.ChiselOut"/> drop spawns at its cell and the bar loses 1
  /// durability per cell outside Creative mode, the stretch stopping early if the bar breaks.</summary>
  /// <returns>The cells cleared; 0 on the client or when the struck cell has no set content.</returns>
  public static int BreakOut(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos pos,
    double yOffset = 0.5
  ) {
    if (
      world.Side != EnumAppSide.Server
      || world.BlockAccessor.GetBlockEntity(pos)
        is not IChiselableMolten { CanBreakOut: true }
    )
      return 0;

    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
    bool creative = byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative;
    int cleared = 0;
    foreach (
      BlockPos cell in Stretch(
        world.BlockAccessor,
        pos,
        ExlibValues.MoltenBarCells
      )
    ) {
      if (
        world.BlockAccessor.GetBlockEntity(cell)
        is not IChiselableMolten { CanBreakOut: true } target
      )
        continue;
      if (target.ChiselOut() is { } drop)
        world.SpawnItemEntity(drop, cell.ToVec3d().Add(0.5, yOffset, 0.5));
      cleared++;
      if (creative || slot?.Itemstack == null)
        continue;
      slot.Itemstack.Collectible.DamageItem(world, byPlayer.Entity, slot, 1);
      if (slot.Itemstack == null)
        break;
    }
    ExSounds.Play(world.Api, pos, ExSounds.AnvilHit, 0.8f);
    return cleared;
  }

  private static bool CanBuildOrBreak(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    world.Claims.TryAccess(
      byPlayer,
      blockSel.Position,
      EnumBlockAccessFlags.BuildOrBreak
    );
}
