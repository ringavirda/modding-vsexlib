using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Assembling;

/// <summary>The server's side of hand assembly: a fix checks the world, takes the fasteners and the part, places the candidate and records its joints; a take-off and a cap's fit and take-off do the same for a piece and an end.</summary>
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
  internal const string Fixed = "exlib-handassembly-fixed";
  internal const string NotFixed = "exlib-handassembly-notfixed";
  internal const string NeedsToolMain = "exlib-handassembly-needstoolmain";
  internal const string CapClosed = "exlib-handassembly-capclosed";
  internal const string CapJoined = "exlib-handassembly-capjoined";

  // Blocks added to the player's picking range.
  private const float RangeSlack = 1f;

  /// <summary>Fixes the candidate <paramref name="fix"/> names.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The sending player.</param>
  /// <param name="fix">The packet; nothing in it is trusted but the aim, the slot and the codes; the dimension is the player's own.</param>
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
      Position = new BlockPos(fix.X, fix.Y, fix.Z, player.Entity.Pos.Dimension),
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
      return Refusal(NeedsTool, ToolNoun(world, spec.Tool, LanguageOf(player)));
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
          FastenerName(world, spec.Fastener, LanguageOf(player)),
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

  /// <summary>Takes the fixed piece at the packet's position off; a block that is not a network node counts as fixed whatever its record holds, and one with no record returns only itself: it and the fasteners of every joint it was part of go to the player, and the cell clears.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The sending player; the tool is read from the active hotbar slot.</param>
  /// <param name="take">The packet; nothing in it is trusted but the position; the dimension is the player's own.</param>
  /// <returns>Null when the piece was taken off; otherwise the refusal to send, with nothing changed.</returns>
  public static AssemblyResult? TakeOff(
    IWorldAccessor world,
    IPlayer player,
    AssemblyTakeOff take
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    var cell = new BlockPos(
      take.X,
      take.Y,
      take.Z,
      player.Entity.Pos.Dimension
    );
    if (!InRange(player, cell))
      return Refusal(OutOfRange);
    if (
      !world.Claims.TryAccess(player, cell, EnumBlockAccessFlags.BuildOrBreak)
    )
      return Refusal(Claimed);

    Block block = accessor.GetBlock(cell);
    BEBehaviorAssembled? record = BEBehaviorAssembled.At(accessor, cell);
    if (
      HandAssemblySpec.Of(block) is not { } spec
      || (block is BlockNetworkNode && record is not { Fixed: true })
    )
      return Refusal(NotFixed);

    bool creative = HandAssemblyGate.IsCreative(player);
    ItemSlot toolSlot = player.InventoryManager.ActiveHotbarSlot;
    if (!creative && !HoldsTool(toolSlot, spec.Tool))
      return Refusal(
        NeedsToolMain,
        ToolNoun(world, spec.Tool, LanguageOf(player))
      );
    if (!player.Entity.Controls.Sneak)
      return Refusal(Fixed);

    var returned = new List<ItemStack>();
    if (record != null) {
      returned.AddRange(record.Spent.Select(s => s.Clone()));
      returned.AddRange(record.Caps.Select(s => s.Clone()));
      returned.AddRange(record.CapSpent.Select(s => s.Clone()));
    }
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      BEBehaviorAssembled? neighbour = BEBehaviorAssembled.At(
        accessor,
        cell.AddCopy(face)
      );
      if (neighbour?.FastenerAt(face.Opposite) is not { } joint)
        continue;
      returned.Add(joint.Clone());
      neighbour.SetFastener(face.Opposite, null);
      neighbour.Blockentity.MarkDirty();
    }

    bool givesPiece =
      block is not BlockNetworkNode node || !node.IsCrackedAt(world, cell);
    if (givesPiece)
      returned.AddRange(block.GetDrops(world, cell, player) ?? []);

    accessor.SetBlock(0, cell);
    accessor.TriggerNeighbourBlockUpdate(cell);

    if (creative)
      return null;
    toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    foreach (ItemStack stack in returned)
      if (!player.InventoryManager.TryGiveItemstack(stack, true))
        world.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
    return null;
  }

  /// <summary>Fits the cap held in the main hand on the end <paramref name="fit"/> names, with the tool in the off hand: takes one cap and the fasteners of one joint, and wears the tool once.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The sending player; the cap is read from the active hotbar slot and the tool from the off hand.</param>
  /// <param name="fit">The packet; nothing in it is trusted but the position, the face, the slot and the code; the dimension is the player's own.</param>
  /// <returns>Null when the cap was fitted; otherwise the refusal to send, with nothing taken. In creative no tool is asked, nothing is taken and the cap carries no fasteners.</returns>
  public static AssemblyResult? FitCap(
    IWorldAccessor world,
    IPlayer player,
    AssemblyCap fit
  ) {
    ItemSlot capSlot = player.InventoryManager.ActiveHotbarSlot;
    if (
      player.InventoryManager.ActiveHotbarSlotNumber != fit.Slot
      || capSlot.Itemstack?.Collectible.Code?.ToString() is not { } held
      || held != fit.CapCode
    )
      return Refusal(PartMoved);

    var cell = new BlockPos(fit.X, fit.Y, fit.Z, player.Entity.Pos.Dimension);
    if (!InRange(player, cell))
      return Refusal(OutOfRange);
    if (
      !world.Claims.TryAccess(player, cell, EnumBlockAccessFlags.BuildOrBreak)
    )
      return Refusal(Claimed);

    IBlockAccessor accessor = world.BlockAccessor;
    BlockFacing face = BlockFacing.ALLFACES[
      GameMath.Clamp(fit.Face, 0, BlockFacing.ALLFACES.Length - 1)
    ];
    if (
      accessor.GetBlock(cell) is not BlockNetworkNode node
      || HandAssemblySpec.Of(node) is not { Cap: { } capPattern } spec
      || BEBehaviorAssembled.At(accessor, cell) is not { Fixed: true } record
      || !HoldsTool(capSlot, capPattern)
      || !node.ShapeConnectorAt(accessor, cell, face)
    )
      return Refusal(NotOffered);
    if (record.CapAt(face) != null)
      return Refusal(Occupied);

    bool creative = HandAssemblyGate.IsCreative(player);
    ItemSlot toolSlot = player.Entity.LeftHandItemSlot;
    if (!creative && !HoldsTool(toolSlot, spec.Tool))
      return Refusal(NeedsTool, ToolNoun(world, spec.Tool, LanguageOf(player)));
    if (HandAssemblyCaps.OpenRefusal(world, node, cell, face) is { } shut)
      return Refusal(shut);

    int cost = spec.PerJoint;
    if (!creative && cost > 0) {
      int carried = ExCarried.Count(player, spec.Fastener);
      if (carried < cost)
        return Refusal(
          NeedsFasteners,
          cost.ToString(),
          FastenerName(world, spec.Fastener, LanguageOf(player)),
          carried.ToString()
        );
    }

    ItemStack cap;
    ItemStack? spent = null;
    if (creative)
      cap = ExactlyOne(capSlot.Itemstack!);
    else {
      if (cost > 0)
        spent = ExCarried.Take(player, spec.Fastener, cost);
      cap = capSlot.TakeOut(1);
      capSlot.MarkDirty();
      toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    }
    record.SetCap(face, cap, spent);
    record.Blockentity.MarkDirty(true);
    RebuildNetworks(world, node, cell);
    return null;
  }

  /// <summary>Serves a plain wrench click on a fixed piece that is not a weeping one: takes off the cap the end nearest the hit carries, returning it and its fasteners. The wrench's wear is the wrench item's, as for any click on a wrench-orientable block.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The clicking player; the wrench is read from the active hotbar slot.</param>
  /// <param name="cell">The piece's position.</param>
  /// <param name="hit">The hit point inside the cell, each axis in 0 to 1; the end whose face centre is nearest is the one served.</param>
  /// <returns>Null when a cap was taken off; otherwise the refusal to send, with nothing changed. An end with no cap answers <see cref="Fixed"/>, whatever the off hand holds.</returns>
  internal static AssemblyResult? Cap(
    IWorldAccessor world,
    IPlayer player,
    BlockPos cell,
    Vec3d hit
  ) {
    if (!InRange(player, cell))
      return Refusal(OutOfRange);
    if (
      !world.Claims.TryAccess(player, cell, EnumBlockAccessFlags.BuildOrBreak)
    )
      return Refusal(Claimed);

    IBlockAccessor accessor = world.BlockAccessor;
    if (
      accessor.GetBlock(cell) is not BlockNetworkNode node
      || HandAssemblySpec.Of(node) is not { } spec
      || BEBehaviorAssembled.At(accessor, cell) is not { Fixed: true } record
    )
      return Refusal(Fixed);

    BlockFacing? face = HandAssemblyCaps.NearestArm(accessor, node, cell, hit);
    if (face == null || record.CapAt(face) == null)
      return Refusal(Fixed);

    bool creative = HandAssemblyGate.IsCreative(player);
    if (
      !creative
      && !HoldsTool(player.InventoryManager.ActiveHotbarSlot, spec.Tool)
    )
      return Refusal(
        NeedsToolMain,
        ToolNoun(world, spec.Tool, LanguageOf(player))
      );
    return TakeCapOff(world, player, node, cell, record, face, creative);
  }

  private static AssemblyResult? TakeCapOff(
    IWorldAccessor world,
    IPlayer player,
    BlockNetworkNode node,
    BlockPos cell,
    BEBehaviorAssembled record,
    BlockFacing face,
    bool creative
  ) {
    ItemStack?[] returned = [record.CapAt(face), record.CapFastenersAt(face)];
    record.SetCap(face, null, null);
    record.Blockentity.MarkDirty(true);
    RebuildNetworks(world, node, cell);
    if (creative)
      return null;
    foreach (ItemStack? stack in returned)
      if (
        stack != null
        && !player.InventoryManager.TryGiveItemstack(stack.Clone(), true)
      )
        world.SpawnItemEntity(stack.Clone(), player.Entity.Pos.XYZ);
    return null;
  }

  private static void RebuildNetworks(
    IWorldAccessor world,
    BlockNetworkNode node,
    BlockPos cell
  ) {
    var networks = world.Api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    networks.RemoveNode(world.BlockAccessor, cell);
    networks.AddNode(
      world.BlockAccessor,
      cell,
      ((INetworkMember)node).NetworkTypeAt(world.BlockAccessor, cell)
    );
  }

  /// <summary>Clears the fastener each neighbour of <paramref name="cell"/> holds for its joint toward it; the fasteners are lost.</summary>
  /// <param name="world">The accessor to read the neighbours from.</param>
  /// <param name="cell">The position of the piece that is gone.</param>
  internal static void ClearRecordsToward(IBlockAccessor world, BlockPos cell) {
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      BEBehaviorAssembled? neighbour = BEBehaviorAssembled.At(
        world,
        cell.AddCopy(face)
      );
      if (neighbour?.FastenerAt(face.Opposite) == null)
        continue;
      neighbour.SetFastener(face.Opposite, null);
      neighbour.Blockentity.MarkDirty();
    }
  }

  /// <summary>Shows <paramref name="player"/> the line of a placement failure code, worded in the player's language.</summary>
  /// <param name="world">The world, for naming a tool.</param>
  /// <param name="player">The player to tell.</param>
  /// <param name="code">The failure code; its line is <c>game:placefailure-</c> and the code.</param>
  /// <param name="args">The arguments the line formats.</param>
  internal static void Tell(
    IWorldAccessor world,
    IServerPlayer player,
    string code,
    params string[] args
  ) => player.SendIngameError(code, Line(LanguageOf(player), code, args));

  /// <summary>The line of a placement failure code in the language <paramref name="langCode"/> (the current one when null), formatted with <paramref name="args"/>.</summary>
  internal static string Line(
    string? langCode,
    string code,
    params string[] args
  ) {
    object[] formatArgs = [.. args];
    return langCode == null
      ? Lang.Get("game:placefailure-" + code, formatArgs)
      : Lang.GetL(langCode, "game:placefailure-" + code, formatArgs);
  }

  /// <summary>The language of <paramref name="player"/>; null on the client, where the current one applies.</summary>
  internal static string? LanguageOf(IPlayer player) =>
    (player as IServerPlayer)?.LanguageCode;

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

  /// <summary>The name of the first item matching <paramref name="pattern"/> in the language <paramref name="langCode"/> (the current one when null), or the pattern itself when none is registered.</summary>
  internal static string ToolName(
    IWorldAccessor world,
    string pattern,
    string? langCode = null
  ) => ItemName(world, pattern, langCode).ToLowerInvariant();

  /// <summary>The noun of the tool <paramref name="pattern"/> matches in the language <paramref name="langCode"/> (the current one when null): the lang entry <c>exlib:handassembly-tool-{word}</c> for the word before the first <c>-</c> of the pattern's path, else <see cref="ToolName"/>.</summary>
  internal static string ToolNoun(
    IWorldAccessor world,
    string pattern,
    string? langCode = null
  ) {
    string path = pattern[(pattern.IndexOf(':') + 1)..];
    int dash = path.IndexOf('-');
    string key = "exlib:handassembly-tool-" + (dash < 0 ? path : path[..dash]);
    bool known =
      langCode != null
      && Lang.AvailableLanguages.TryGetValue(langCode, out var service)
        ? service.HasTranslation(key, false)
        : Lang.HasTranslation(key, false);
    if (!known)
      return ToolName(world, pattern, langCode);
    return langCode == null ? Lang.Get(key) : Lang.GetL(langCode, key);
  }

  internal static string FastenerName(
    IWorldAccessor world,
    string code,
    string? langCode = null
  ) => ItemName(world, code, langCode);

  private static string ItemName(
    IWorldAccessor world,
    string pattern,
    string? langCode
  ) =>
    world.Items.FirstOrDefault(i =>
      i.Code != null && WildcardUtil.Match(pattern, i.Code.ToString())
    )
      is { Code: { } code } item
      ? NameKey(code, item.ItemClass, langCode)
      : pattern;

  private static string NameKey(
    AssetLocation code,
    EnumItemClass itemClass,
    string? langCode
  ) {
    string key = code.Domain + ":" + itemClass.Name() + "-" + code.Path;
    return langCode == null
      ? Lang.GetMatching(key)
      : Lang.GetMatchingL(langCode, key);
  }

  internal static bool HoldsTool(ItemSlot slot, string pattern) =>
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
    if (spent != null) {
      if (candidate.Block is not BlockNetworkNode)
        record.Joint = spent;
      else
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
    record.Blockentity.MarkDirty();
  }
}
