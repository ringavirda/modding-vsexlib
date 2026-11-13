using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Assembling;

/// <summary>The server's side of a fix: checks the world, takes the fasteners and the part, places the candidate and records its joints.</summary>
internal static class HandAssemblyServer {
  internal const string NeedsTool = "exlib-handassembly-needstool";
  internal const string NeedsFasteners = "exlib-handassembly-needfasteners";
  internal const string NotOffered = "exlib-handassembly-notoffered";
  internal const string PartMoved = "exlib-handassembly-partmoved";
  internal const string OutOfRange = "exlib-handassembly-outofrange";
  internal const string Claimed = "exlib-handassembly-claimed";
  internal const string Unloaded = "exlib-handassembly-unloaded";
  internal const string Occupied = "exlib-handassembly-occupied";
  internal const string Refused = "exlib-handassembly-refused";

  // Blocks added to the player's picking range.
  private const float RangeSlack = 1f;

  /// <summary>Fixes the candidate <paramref name="fix"/> names.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The sending player.</param>
  /// <param name="fix">The packet; nothing in it is trusted but the aim, the slot and the codes.</param>
  /// <returns>Null when the part was placed; otherwise the refusal to send, with nothing taken.</returns>
  public static AssemblyResult? Fix(
    IWorldAccessor world,
    IPlayer player,
    AssemblyFix fix
  ) {
    ItemSlot partSlot = player.InventoryManager.ActiveHotbarSlot;
    if (
      player.InventoryManager.ActiveHotbarSlotNumber != fix.Slot
      || partSlot.Itemstack?.Block is not { } part
      || part.Code.ToString() != fix.PartCode
      || HandAssemblySpec.Of(part) is not { } spec
    )
      return Refusal(PartMoved);

    BlockFacing face = BlockFacing.ALLFACES[
      GameMath.Clamp(fix.Face, 0, BlockFacing.ALLFACES.Length - 1)
    ];
    var selection = new BlockSelection {
      Position = new BlockPos(fix.X, fix.Y, fix.Z, fix.Dimension),
      Face = face,
      HitPosition = new Vec3d(fix.HitX, fix.HitY, fix.HitZ),
    };
    HandAssemblyTarget target = HandAssemblyTarget.Resolve(
      world.BlockAccessor,
      selection,
      part
    );
    BlockPos cell = target.Cell;

    if (!InRange(player, cell))
      return Refusal(OutOfRange);
    if (
      !world.Claims.TryAccess(player, cell, EnumBlockAccessFlags.BuildOrBreak)
    )
      return Refusal(Claimed);

    bool creative = HandAssemblyGate.IsCreative(player);
    ItemSlot toolSlot = player.Entity.LeftHandItemSlot;
    if (!creative && !HoldsTool(toolSlot, spec.Tool))
      return Refusal(NeedsTool, ToolName(world, spec.Tool));
    if (!NeighbourhoodLoaded(world.BlockAccessor, cell))
      return Refusal(Unloaded);

    Block? requested = world.GetBlock(new AssetLocation(fix.CandidateCode));
    if (requested == null)
      return Refusal(NotOffered);
    if (!world.BlockAccessor.GetBlock(cell).IsReplacableBy(requested))
      return Refusal(Occupied);

    AssemblyCandidate? candidate = HandAssemblyCandidates
      .Of(world, player, part, selection)
      .FirstOrDefault(c => c.Block.Code.Equals(requested.Code));
    if (candidate == null)
      return Refusal(
        HandAssemblyCandidates.PartRefusal(part, world, cell, requested)
          ?? NotOffered
      );

    string networkRefusal = "";
    if (
      candidate.Block is BlockNetworkNode node
      && world.Api.ModLoader.GetModSystem<BlockNetworkModSystem>() is { } system
      && !system.CanPlaceMember(
        world.BlockAccessor,
        cell,
        candidate.Block,
        node.NetworkType,
        ref networkRefusal
      )
    )
      return Refusal(networkRefusal);

    int cost = candidate.Joints * spec.PerJoint;
    if (!creative && cost > 0) {
      int carried = ExCarried.Count(player, spec.Fastener);
      if (carried < cost)
        return Refusal(
          NeedsFasteners,
          cost.ToString(),
          FastenerName(world, spec.Fastener),
          carried.ToString()
        );
    }

    ItemStack? spent = null;
    ItemStack placed;
    if (creative)
      placed = ExactlyOne(partSlot.Itemstack!);
    else {
      if (cost > 0)
        spent = ExCarried.Take(player, spec.Fastener, cost);
      placed = partSlot.TakeOut(1);
      partSlot.MarkDirty();
      toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    }

    Place(world, player, candidate, selection, cell, placed, spent, spec);
    return null;
  }

  /// <summary>Places the first candidate of the aim, fixed and with no fastener recorded; the vanilla creative path.</summary>
  /// <returns>False, with <paramref name="failureCode"/> set, when the aim offers no placement.</returns>
  public static bool PlaceFirst(
    IWorldAccessor world,
    IPlayer player,
    Block part,
    ItemStack itemstack,
    BlockSelection selection,
    ref string failureCode
  ) {
    AssemblyCandidate? first = HandAssemblyCandidates
      .Of(world, player, part, selection)
      .FirstOrDefault();
    if (first == null) {
      failureCode = NotOffered;
      return false;
    }

    BlockPos cell = HandAssemblyTarget
      .Resolve(world.BlockAccessor, selection, part)
      .Cell;
    Place(
      world,
      player,
      first,
      selection,
      cell,
      ExactlyOne(itemstack),
      null,
      HandAssemblySpec.Of(part)!
    );
    return true;
  }

  /// <summary>The line-and-arguments of a refusal; the client formats <c>game:placefailure-</c> and the code with the arguments.</summary>
  internal static AssemblyResult Refusal(string code, params string[] args) =>
    new() { Code = code, Args = args };

  /// <summary>The name of the first item matching <paramref name="pattern"/>, or the pattern itself when none is registered.</summary>
  internal static string ToolName(IWorldAccessor world, string pattern) =>
    ItemName(world, pattern).ToLowerInvariant();

  private static string FastenerName(IWorldAccessor world, string code) =>
    ItemName(world, code);

  private static string ItemName(IWorldAccessor world, string pattern) =>
    world.Items.FirstOrDefault(i =>
      i.Code != null && WildcardUtil.Match(pattern, i.Code.ToString())
    )
      is { } item
      ? new ItemStack(item).GetName()
      : pattern;

  private static bool HoldsTool(ItemSlot slot, string pattern) =>
    slot.Itemstack?.Collectible.Code is { } code
    && WildcardUtil.Match(pattern, code.ToString());

  private static bool InRange(IPlayer player, BlockPos cell) =>
    player
      .Entity.Pos.XYZ.AddCopy(player.Entity.LocalEyePos)
      .DistanceTo(cell.X + 0.5, cell.Y + 0.5, cell.Z + 0.5)
    <= player.WorldData.PickingRange + RangeSlack;

  private static bool NeighbourhoodLoaded(
    IBlockAccessor world,
    BlockPos cell
  ) =>
    world.GetChunkAtBlockPos(cell) != null
    && BlockFacing.ALLFACES.All(face =>
      world.GetChunkAtBlockPos(cell.AddCopy(face)) != null
    );

  private static ItemStack ExactlyOne(ItemStack stack) {
    ItemStack one = stack.Clone();
    one.StackSize = 1;
    return one;
  }

  private static void Place(
    IWorldAccessor world,
    IPlayer player,
    AssemblyCandidate candidate,
    BlockSelection selection,
    BlockPos cell,
    ItemStack placed,
    ItemStack? spent,
    HandAssemblySpec spec
  ) {
    if (candidate.Block is BlockNetworkNode)
      BlockNetworkNode.StoreChoices(world, cell, [candidate.Token]);
    candidate.Block.DoPlaceBlock(
      world,
      player,
      new BlockSelection {
        Position = cell.Copy(),
        Face = selection.Face,
        HitPosition = selection.HitPosition,
        DidOffset = true,
      },
      placed
    );

    BEBehaviorAssembled? record = world
      .BlockAccessor.GetBlockEntity(cell)
      ?.GetBehavior<BEBehaviorAssembled>();
    if (record == null)
      return;
    record.Fixed = true;
    record.Blockentity.MarkDirty();
    if (spent == null)
      return;

    if (candidate.Block is not BlockNetworkNode) {
      record.Joint = spent;
      return;
    }
    foreach (
      BlockFacing arm in candidate.Arms.Where(a =>
        !candidate.OpenArms.Contains(a)
      )
    ) {
      ItemStack joint = spent.Clone();
      joint.StackSize = spec.PerJoint;
      record.SetFastener(arm, joint);
    }
  }
}
