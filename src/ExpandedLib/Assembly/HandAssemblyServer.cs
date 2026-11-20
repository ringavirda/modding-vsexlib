using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
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
  internal const string NoFit = "exlib-handassembly-nofit";
  internal const string NeedsCompanion = "exlib-handassembly-needcompanion";
  internal const string CapClosed = "exlib-handassembly-capclosed";
  internal const string CapJoined = "exlib-handassembly-capjoined";
  internal const string CapNoRoom = "exlib-handassembly-capnoroom";
  internal const string CapInWay = "exlib-handassembly-capinway";

  // Blocks added to the player's picking range.
  private const float RangeSlack = 1f;

  [System.ThreadStatic]
  private static bool _keepRecords;

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
    AssemblyResult? refusal = FixPart(world, player, fix);
    if (refusal != null)
      HandAssemblyLog.Refused(
        world,
        player,
        refusal,
        player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Code,
        new BlockPos(fix.X, fix.Y, fix.Z, player.Entity.Pos.Dimension)
      );
    return refusal;
  }

  private static AssemblyResult? FixPart(
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
    if (HandAssemblyCaps.InTheWay(world.BlockAccessor, cell) != null)
      return Refusal(CapInWay);

    IReadOnlyList<AssemblyCandidate> offered = HandAssemblyCandidates.Of(
      world,
      player,
      part,
      selection
    );
    AssemblyCandidate? candidate = offered.FirstOrDefault(c =>
      c.Block.Code.Equals(requested.Code)
    );
    if (candidate == null)
      return Refusal(
        offered.Count > 0
          ? HandAssemblyCandidates.PartRefusal(part, world, cell, requested)
            ?? NotOffered
          : HandAssemblyCandidates.EmptyRefusal(world, player, part, selection)
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

    Place(
      world,
      player,
      candidate,
      selection,
      cell,
      placed,
      spent,
      creative,
      spec
    );
    HandAssemblyLog.Write(
      world,
      player,
      "fix",
      candidate.Block.Code,
      cell,
      candidate.Block is BlockNetworkNode ? Joined(candidate) : [],
      [],
      spent == null ? [] : [spent]
    );
    PlaySuccess(world, cell, toolSlot);
    return null;
  }

  /// <summary>Takes the fixed piece at the packet's position off; a block that is not a network node counts as fixed whatever its record holds, and one with no record returns only itself. In survival the piece and the fasteners of every joint it was part of (<see cref="TakeOffReturns"/>) go to the player, what the pack cannot hold spawns at the player's feet, and the records of those joints clear; the player gets the line of what came back in the Info log chat, in the player's language. In creative nothing returns and no record is lost: the records the piece holds move onto the neighbours across their joints, and the records the neighbours hold toward the cell stay.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The sending player; the tool is read from the active hotbar slot.</param>
  /// <param name="take">The packet; nothing in it is trusted but the position; the dimension is the player's own.</param>
  /// <returns>Null when the piece was taken off; otherwise the refusal to send, with nothing changed.</returns>
  public static AssemblyResult? TakeOff(
    IWorldAccessor world,
    IPlayer player,
    AssemblyTakeOff take
  ) {
    AssemblyResult? refusal = TakeOffPiece(world, player, take);
    if (refusal != null) {
      var cell = new BlockPos(
        take.X,
        take.Y,
        take.Z,
        player.Entity.Pos.Dimension
      );
      HandAssemblyLog.Refused(
        world,
        player,
        refusal,
        world.BlockAccessor.GetBlock(cell).Code,
        cell
      );
    }
    return refusal;
  }

  private static AssemblyResult? TakeOffPiece(
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

    BlockPos principal = HandFitAim.PrincipalOf(accessor, cell);
    if (
      accessor.GetBlock(principal) is IHandFitCellTarget fitTarget
      && fitTarget.FittedAt(
        accessor,
        principal,
        new HandFitPoint(cell, new Vec3d(take.HitX, take.HitY, take.HitZ))
      )
        is not null
    )
      return TakeOffFit(world, player, take, cell, principal, fitTarget);

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
    IReadOnlyList<ItemStack> back = [];
    IReadOnlyList<BlockFacing> joints = JointFaces(accessor, cell);
    if (creative)
      KeepRecords(accessor, cell, record);
    else {
      back = TakeOffReturns(accessor, cell).Stacks;
      returned.AddRange(back);
      ClearRecordsToward(accessor, cell);
      bool givesPiece =
        block is not BlockNetworkNode node || !node.IsCrackedAt(world, cell);
      if (givesPiece)
        returned.AddRange(block.GetDrops(world, cell, player) ?? []);
    }

    _keepRecords = creative;
    try {
      accessor.SetBlock(0, cell);
    } finally {
      _keepRecords = false;
    }
    accessor.TriggerNeighbourBlockUpdate(cell);

    HandAssemblyLog.Write(
      world,
      player,
      "takeoff",
      block.Code,
      cell,
      joints,
      back,
      []
    );
    TellTookOff(player, block, back);
    PlaySuccess(world, cell, toolSlot);
    if (creative)
      return null;
    toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    foreach (ItemStack stack in returned)
      if (!player.InventoryManager.TryGiveItemstack(stack, true))
        world.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
    return null;
  }

  private static AssemblyResult? TakeOffFit(
    IWorldAccessor world,
    IPlayer player,
    AssemblyTakeOff take,
    BlockPos cell,
    BlockPos principal,
    IHandFitCellTarget target
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    Block block = accessor.GetBlock(principal);
    if (HandAssemblySpec.Of(block) is not { } spec)
      return Refusal(NotFixed);

    bool creative = HandAssemblyGate.IsCreative(player);
    ItemSlot toolSlot = player.InventoryManager.ActiveHotbarSlot;
    var at = new HandFitPoint(cell, new Vec3d(take.HitX, take.HitY, take.HitZ));
    if (target.FittedAt(accessor, principal, at) is not { } removal)
      return Refusal(NotFixed);
    string tool = removal.Tool ?? spec.Tool;
    if (!creative && !HoldsTool(toolSlot, tool))
      return Refusal(NeedsToolMain, ToolNoun(world, tool, LanguageOf(player)));
    if (!player.Entity.Controls.Sneak)
      return Refusal(Fixed);
    if (!target.TakeOff(world, principal, at, player))
      return Refusal(NotFixed);

    var fasteners = new List<ItemStack>();
    if (
      !creative
      && removal.Fasteners > 0
      && world.GetItem(new AssetLocation(spec.Fastener)) is { } fastener
    )
      fasteners.Add(new ItemStack(fastener, removal.Fasteners));
    List<ItemStack> back = creative
      ? []
      : [.. removal.Parts.Select(p => p.Clone()), .. fasteners];

    HandAssemblyLog.Write(
      world,
      player,
      "takeoff",
      block.Code,
      cell,
      [],
      back,
      []
    );
    TellTookOff(player, removal.Parts, fasteners);
    PlaySuccess(world, cell, toolSlot);
    if (creative)
      return null;
    toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    foreach (ItemStack stack in back)
      if (!player.InventoryManager.TryGiveItemstack(stack, true))
        world.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
    return null;
  }

  private static void TellTookOff(
    IPlayer player,
    IReadOnlyList<ItemStack> parts,
    IReadOnlyList<ItemStack> fasteners
  ) {
    if (player is not IServerPlayer serverPlayer)
      return;
    string? lang = LanguageOf(player);
    string Say(string key, params object[] args) =>
      lang == null ? Lang.Get(key, args) : Lang.GetL(lang, key, args);
    string piece = string.Join(
      ", ",
      Grouped(parts, lang)
        .Select(g => Say("exlib:handassembly-hud-stack", g.Count, g.Name))
    );
    string line =
      fasteners.Count == 0
        ? Say("exlib:handassembly-hud-tookoff-bare", piece)
        : Say(
          "exlib:handassembly-hud-tookoff",
          piece,
          HandAssemblyHud.Stacks(Say, Grouped(fasteners, lang))
        );
    serverPlayer.SendMessage(
      GlobalConstants.InfoLogChatGroup,
      line,
      EnumChatType.Notification
    );
  }

  private static void TellTookOff(
    IPlayer player,
    Block block,
    IReadOnlyList<ItemStack> back
  ) {
    if (player is not IServerPlayer serverPlayer)
      return;
    string? lang = LanguageOf(player);
    string piece = NameKey(block.Code, EnumItemClass.Block, lang);
    string Say(string key, params object[] args) =>
      lang == null ? Lang.Get(key, args) : Lang.GetL(lang, key, args);
    string line =
      back.Count == 0
        ? Say("exlib:handassembly-hud-tookoff-bare", piece)
        : Say(
          "exlib:handassembly-hud-tookoff",
          piece,
          HandAssemblyHud.Stacks(Say, Grouped(back, lang))
        );
    serverPlayer.SendMessage(
      GlobalConstants.InfoLogChatGroup,
      line,
      EnumChatType.Notification
    );
  }

  /// <summary>The stacks grouped by item, in the order each item first appears.</summary>
  /// <param name="stacks">The stacks to group.</param>
  /// <param name="langCode">The language of the names; the current one when null.</param>
  /// <returns>Each item's name and the sum of its stack sizes.</returns>
  internal static IReadOnlyList<(string Name, int Count)> Grouped(
    IEnumerable<ItemStack> stacks,
    string? langCode = null
  ) =>
    [
      .. stacks
        .GroupBy(s => s.Collectible.Code.ToString())
        .Select(g =>
          (
            NameKey(g.First().Collectible.Code, g.First().Class, langCode),
            g.Sum(s => s.StackSize)
          )
        ),
    ];

  /// <summary>The faces of the piece at <paramref name="cell"/> that carry a joint: its own record holds a fastener or a free mark there, or the neighbour across it holds one toward the cell.</summary>
  internal static IReadOnlyList<BlockFacing> JointFaces(
    IBlockAccessor accessor,
    BlockPos cell
  ) {
    BEBehaviorAssembled? own = BEBehaviorAssembled.At(accessor, cell);
    var faces = new List<BlockFacing>();
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      BEBehaviorAssembled? across = BEBehaviorAssembled.At(
        accessor,
        cell.AddCopy(face)
      );
      if (
        (own != null && (own.FastenerAt(face) != null || own.FreeAt(face)))
        || (
          across != null
          && (
            across.FastenerAt(face.Opposite) != null
            || across.FreeAt(face.Opposite)
          )
        )
      )
        faces.Add(face);
    }
    return faces;
  }

  /// <summary>Writes the debug line of a fixed piece a player breaks or a burst removes, with the fasteners lost: its own joints', its fitted caps' and those its neighbours hold toward it, and the caps too when the player is in creative, where none drops.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The breaking player; null for a break with none.</param>
  /// <param name="block">The piece.</param>
  /// <param name="cell">The piece's position.</param>
  /// <param name="record">The piece's record, read before the break clears the records toward it.</param>
  internal static void LogBreak(
    IWorldAccessor world,
    IPlayer? player,
    Block block,
    BlockPos cell,
    BEBehaviorAssembled record
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    IEnumerable<ItemStack> lost = [.. record.Spent, .. record.CapSpent];
    if (player != null && HandAssemblyGate.IsCreative(player))
      lost = [.. lost, .. record.Caps];
    foreach (BlockFacing face in BlockFacing.ALLFACES)
      if (
        BEBehaviorAssembled
          .At(accessor, cell.AddCopy(face))
          ?.FastenerAt(face.Opposite) is { } toward
      )
        lost = [.. lost, toward];
    HandAssemblyLog.Write(
      world,
      player,
      "break",
      block.Code,
      cell,
      JointFaces(accessor, cell),
      [],
      lost
    );
  }

  /// <summary>The stacks a survival take-off of the piece at <paramref name="cell"/> gives besides the piece, and whether any joint of it is owed nothing.</summary>
  /// <param name="accessor">The accessor to read the records from.</param>
  /// <param name="cell">The position of the fixed piece.</param>
  /// <returns>Stacks: clones of the fasteners its own joints spent, its caps and their fasteners, and the fasteners each neighbour holds for its joint toward the cell. AnyFree: whether the piece's own record or a neighbour's toward the cell marks a joint free, made in creative.</returns>
  internal static (
    IReadOnlyList<ItemStack> Stacks,
    bool AnyFree
  ) TakeOffReturns(IBlockAccessor accessor, BlockPos cell) {
    var stacks = new List<ItemStack>();
    bool anyFree = false;
    if (BEBehaviorAssembled.At(accessor, cell) is { } record) {
      stacks.AddRange(record.Spent.Select(s => s.Clone()));
      stacks.AddRange(record.Caps.Select(s => s.Clone()));
      stacks.AddRange(record.CapSpent.Select(s => s.Clone()));
      anyFree = record.HasFree;
    }
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      if (
        BEBehaviorAssembled.At(accessor, cell.AddCopy(face))
        is not { } neighbour
      )
        continue;
      if (neighbour.FastenerAt(face.Opposite) is { } joint)
        stacks.Add(joint.Clone());
      anyFree |= neighbour.FreeAt(face.Opposite);
    }
    return (stacks, anyFree);
  }

  private static void KeepRecords(
    IBlockAccessor accessor,
    BlockPos cell,
    BEBehaviorAssembled? record
  ) {
    if (record == null)
      return;
    BEBehaviorAssembled? only = null;
    BlockFacing? onlyFace = null;
    int carriers = 0;
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      if (BEBehaviorAssembled.At(accessor, cell.AddCopy(face)) is not { } n)
        continue;
      carriers++;
      only = n;
      onlyFace = face;
      if (record.FastenerAt(face) is { } own)
        Add(n, face.Opposite, own);
    }
    if (record.Joint is { } joint && carriers == 1 && only != null)
      Add(only, onlyFace!.Opposite, joint);
  }

  private static void Add(
    BEBehaviorAssembled holder,
    BlockFacing face,
    ItemStack stack
  ) {
    ItemStack kept = stack.Clone();
    if (
      holder.FastenerAt(face) is { } held
      && held.Collectible == kept.Collectible
    )
      kept.StackSize += held.StackSize;
    holder.SetFastener(face, kept);
    holder.Blockentity.MarkDirty();
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
    AssemblyResult? refusal = FitCapOn(world, player, fit);
    if (refusal != null) {
      var cell = new BlockPos(fit.X, fit.Y, fit.Z, player.Entity.Pos.Dimension);
      HandAssemblyLog.Refused(
        world,
        player,
        refusal,
        world.BlockAccessor.GetBlock(cell).Code,
        cell
      );
    }
    return refusal;
  }

  private static AssemblyResult? FitCapOn(
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
    HandAssemblyLog.Write(
      world,
      player,
      "capfit",
      node.Code,
      cell,
      [face],
      [],
      spent == null ? [] : [spent]
    );
    PlaySuccess(world, cell, toolSlot);
    return null;
  }

  /// <summary>Fits the item held in the main hand onto the placed block <paramref name="fit"/> names, with the tool in the off hand: asks the block's <see cref="IHandFitTarget"/> to swap, spends <see cref="HandAssemblySpec.PerFit"/> fasteners and wears the tool once.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="player">The sending player; the item is read from the active hotbar slot and the tool from the off hand.</param>
  /// <param name="fit">The packet; nothing in it is trusted but the position, the slot and the code; the dimension is the player's own.</param>
  /// <returns>Null when the fit was made; otherwise the refusal to send, with nothing spent. In creative no tool is asked and nothing is spent. The fasteners spent are added to <see cref="BEBehaviorAssembled.Joint"/> of the swapped block's entity when it carries a record.</returns>
  public static AssemblyResult? Fit(
    IWorldAccessor world,
    IPlayer player,
    AssemblyFit fit
  ) {
    AssemblyResult? refusal = FitOn(world, player, fit);
    if (refusal != null) {
      var cell = new BlockPos(fit.X, fit.Y, fit.Z, player.Entity.Pos.Dimension);
      HandAssemblyLog.Refused(
        world,
        player,
        refusal,
        world.BlockAccessor.GetBlock(cell).Code,
        cell
      );
    }
    return refusal;
  }

  private static AssemblyResult? FitOn(
    IWorldAccessor world,
    IPlayer player,
    AssemblyFit fit
  ) {
    ItemSlot heldSlot = player.InventoryManager.ActiveHotbarSlot;
    if (
      player.InventoryManager.ActiveHotbarSlotNumber != fit.Slot
      || heldSlot.Itemstack?.Collectible.Code?.ToString() is not { } held
      || held != fit.ItemCode
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
    var hit = new Vec3d(fit.HitX, fit.HitY, fit.HitZ);
    if (
      HandFitAim.OfCell(
        accessor,
        cell,
        hit,
        heldSlot.Itemstack,
        out string? cellWhy
      ) is { } cellAim
    )
      return FitCell(world, player, heldSlot, cell, cellAim);
    if (cellWhy != null)
      return Refusal(cellWhy);
    if (
      HandFitAim.Of(accessor, cell, heldSlot.Itemstack, out string? why)
        is not { } aim
      || HandAssemblySpec.Of(accessor.GetBlock(cell)) is not { } spec
    )
      return Refusal(why ?? NoFit);

    bool creative = HandAssemblyGate.IsCreative(player);
    ItemSlot toolSlot = player.Entity.LeftHandItemSlot;
    if (!creative && !HoldsTool(toolSlot, spec.Tool))
      return Refusal(NeedsTool, ToolNoun(world, spec.Tool, LanguageOf(player)));

    int cost = creative ? 0 : spec.PerFit;
    if (cost > 0) {
      int carried = ExCarried.Count(player, spec.Fastener);
      if (carried < cost)
        return Refusal(
          NeedsFasteners,
          cost.ToString(),
          FastenerName(world, spec.Fastener, LanguageOf(player)),
          carried.ToString()
        );
    }

    string failure = "";
    if (!aim.Target.Fit(world, cell, player, heldSlot, ref failure))
      return Refusal(string.IsNullOrEmpty(failure) ? NoFit : failure);

    ItemStack? spent =
      cost > 0 ? ExCarried.Take(player, spec.Fastener, cost) : null;
    if (!creative)
      toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    if (spent != null && BEBehaviorAssembled.At(accessor, cell) is { } record) {
      if (record.Joint is { } before && before.Collectible == spent.Collectible)
        spent.StackSize += before.StackSize;
      record.Joint = spent;
      record.Blockentity.MarkDirty(true);
    }
    HandAssemblyLog.Write(
      world,
      player,
      "fit",
      accessor.GetBlock(cell).Code,
      cell,
      [],
      [],
      spent == null ? [] : [spent]
    );
    PlaySuccess(world, cell, toolSlot);
    return null;
  }

  private static AssemblyResult? FitCell(
    IWorldAccessor world,
    IPlayer player,
    ItemSlot heldSlot,
    BlockPos cell,
    HandFitAim.Cell aim
  ) {
    if (HandAssemblySpec.Of(aim.PrincipalBlock) is not { } spec)
      return Refusal(NoFit);

    bool creative = HandAssemblyGate.IsCreative(player);
    ItemSlot toolSlot = player.Entity.LeftHandItemSlot;
    if (!creative && !HoldsTool(toolSlot, spec.Tool))
      return Refusal(NeedsTool, ToolNoun(world, spec.Tool, LanguageOf(player)));

    string heldCode = heldSlot.Itemstack.Collectible.Code.ToString();
    IReadOnlyList<HandFitCompanion> companions =
    [
      .. aim
        .Plan.Companions.GroupBy(c => c.Code)
        .Select(g => new HandFitCompanion(g.Key, g.Sum(c => c.Count))),
    ];
    if (!creative)
      foreach (HandFitCompanion companion in companions) {
        int needed =
          companion.Count
          + (WildcardUtil.Match(companion.Code, heldCode) ? 1 : 0);
        int carried = ExCarried.Count(player, companion.Code);
        if (carried < needed)
          return Refusal(
            NeedsCompanion,
            needed.ToString(),
            ItemName(world, companion.Code, LanguageOf(player)),
            carried.ToString()
          );
      }

    int cost = creative ? 0 : aim.Plan.Fasteners;
    if (cost > 0) {
      int carried = ExCarried.Count(player, spec.Fastener);
      if (carried < cost)
        return Refusal(
          NeedsFasteners,
          cost.ToString(),
          FastenerName(world, spec.Fastener, LanguageOf(player)),
          carried.ToString()
        );
    }

    string failure = "";
    if (
      !aim.Target.Fit(
        world,
        aim.Principal,
        aim.At,
        player,
        heldSlot,
        ref failure
      )
    )
      return Refusal(string.IsNullOrEmpty(failure) ? NoFit : failure);

    var spent = new List<ItemStack>();
    if (!creative) {
      foreach (HandFitCompanion companion in companions)
        if (
          ExCarried.Take(player, companion.Code, companion.Count) is { } taken
        )
          spent.Add(taken);
      if (cost > 0 && ExCarried.Take(player, spec.Fastener, cost) is { } bolts)
        spent.Add(bolts);
      toolSlot.Itemstack.Collectible.DamageItem(world, player.Entity, toolSlot);
    }
    HandAssemblyLog.Write(
      world,
      player,
      "fit",
      aim.PrincipalBlock.Code,
      cell,
      [],
      [],
      spent
    );
    PlaySuccess(world, cell, toolSlot);
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
    AssemblyResult? refusal = TakeCap(world, player, cell, hit);
    if (refusal != null)
      HandAssemblyLog.Refused(
        world,
        player,
        refusal,
        world.BlockAccessor.GetBlock(cell).Code,
        cell
      );
    return refusal;
  }

  /// <summary>The end of a fixed piece a wrench click at <paramref name="hit"/> takes the cap off.</summary>
  /// <param name="accessor">The world's block accessor.</param>
  /// <param name="node">The piece's block.</param>
  /// <param name="cell">The piece's position.</param>
  /// <param name="record">The piece's assembly record.</param>
  /// <param name="hit">The hit point inside the cell, each axis in 0 to 1.</param>
  /// <returns>The arm whose face centre is nearest <paramref name="hit"/>; null when the piece has no arm or that arm carries no cap.</returns>
  internal static BlockFacing? CappedEnd(
    IBlockAccessor accessor,
    BlockNetworkNode node,
    BlockPos cell,
    BEBehaviorAssembled record,
    Vec3d hit
  ) =>
    HandAssemblyCaps.NearestArm(accessor, node, cell, hit) is { } face
    && record.CapAt(face) != null
      ? face
      : null;

  private static AssemblyResult? TakeCap(
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

    if (CappedEnd(accessor, node, cell, record, hit) is not { } face)
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
    HandAssemblyLog.Write(
      world,
      player,
      "captakeoff",
      node.Code,
      cell,
      [face],
      creative ? [] : returned.OfType<ItemStack>(),
      []
    );
    PlaySuccess(world, cell, player.InventoryManager.ActiveHotbarSlot);
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

  /// <summary>Clears the fastener each neighbour of <paramref name="cell"/> holds for its joint toward it, and its free mark; the fasteners are lost, so a caller that returns them reads them first. Does nothing while a creative take-off removes its piece, which keeps the neighbours' records.</summary>
  /// <param name="world">The accessor to read the neighbours from.</param>
  /// <param name="cell">The position of the piece that is gone.</param>
  internal static void ClearRecordsToward(IBlockAccessor world, BlockPos cell) {
    if (_keepRecords)
      return;
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      BEBehaviorAssembled? neighbour = BEBehaviorAssembled.At(
        world,
        cell.AddCopy(face)
      );
      if (
        neighbour == null
        || (
          neighbour.FastenerAt(face.Opposite) == null
          && !neighbour.FreeAt(face.Opposite)
        )
      )
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
      true,
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

  internal static string ItemName(
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

  private const string LatchSound = "sounds/effect/latch";
  private const string AnvilSound = "game:sounds/block/anvil";

  private static void PlaySuccess(
    IWorldAccessor world,
    BlockPos cell,
    ItemSlot tool
  ) {
    string sound = CraftSequenceRegistry
      .Shared.ToolDefault(tool.Itemstack?.Collectible.Code.ToString())
      ?.Sound switch {
        null or LatchSound or "game:" + LatchSound => AnvilSound,
        string own => own,
      };
    world.PlaySoundAt(
      new AssetLocation(sound),
      cell.X + 0.5,
      cell.Y + 0.5,
      cell.Z + 0.5,
      null,
      true,
      16
    );
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

  private static IEnumerable<BlockFacing> Joined(AssemblyCandidate candidate) =>
    candidate.Arms.Where(a => !candidate.OpenArms.Contains(a));

  private static void Place(
    IWorldAccessor world,
    IPlayer player,
    AssemblyCandidate candidate,
    BlockSelection selection,
    BlockPos cell,
    ItemStack placed,
    ItemStack? spent,
    bool free,
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
    if (free) {
      if (candidate.Block is not BlockNetworkNode) {
        if (candidate.Joints > 0)
          record.JointFree = true;
      } else
        foreach (BlockFacing arm in Joined(candidate))
          record.SetFree(arm, true);
    } else if (spent != null) {
      if (candidate.Block is not BlockNetworkNode)
        record.Joint = spent;
      else
        foreach (BlockFacing arm in Joined(candidate)) {
          ItemStack joint = spent.Clone();
          joint.StackSize = spec.PerJoint;
          record.SetFastener(arm, joint);
        }
    }
    record.Blockentity.MarkDirty();
  }
}
