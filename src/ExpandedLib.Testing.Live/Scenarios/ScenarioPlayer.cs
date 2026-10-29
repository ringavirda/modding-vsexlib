using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>The server-side player a run's cases act through, built from the engine's own classes
/// with no connection and marked connected so the engine reads its game mode (scenarios.md, The
/// scenario player).</summary>
internal sealed class ScenarioPlayer : IHandRelease {
  public const string Uid = "exlib-scenario";
  private const string Name = "Scenario";
  private const int ParkHeight = 16;

  private readonly ServerMain _server;
  private readonly ServerWorldPlayerData _data;
  private readonly BlockPos _park;
  private readonly PlayerAntiAbuseMonitor? _abuse;
  private ItemStack? _filledHand;
  private EnumHandInteract? _handUse;
  private int _uses;
  private int _breaks;
  private int _slotChanges;

  private ScenarioPlayer(
    ServerMain server,
    ServerWorldPlayerData data,
    ServerPlayer player,
    BlockPos park,
    PlayerAntiAbuseMonitor? abuse
  ) {
    _abuse = abuse;
    _server = server;
    _data = data;
    _park = park;
    Player = player;
    server.EventManager.DidUseBlock += CountUse;
    server.EventManager.DidBreakBlock += CountBreak;
    server.EventManager.AfterActiveSlotChanged += CountSlotChange;
  }

  /// <summary>How many did-use events the server has fired for any player since the scenario player was built.</summary>
  public int Uses => _uses;

  /// <summary>Whether the last <see cref="UseStart"/> placed the held block, which fires no did-use
  /// event.</summary>
  public bool Placed { get; private set; }

  private void CountUse(IServerPlayer player, BlockSelection selection) =>
    _uses++;

  /// <summary>How many after-active-slot-changed events the server has fired for any player since the scenario player was built.</summary>
  public int SlotChanges => _slotChanges;

  private void CountSlotChange(
    IServerPlayer player,
    ActiveSlotChangeEventArgs args
  ) => _slotChanges++;

  /// <summary>How many did-break events the server has fired for any player since the scenario player was built.</summary>
  public int Breaks => _breaks;

  private void CountBreak(
    IServerPlayer player,
    int oldBlockId,
    BlockSelection selection
  ) => _breaks++;

  /// <summary>The engine's player the steps act through.</summary>
  public ServerPlayer Player { get; }

  /// <summary>The cell the player waits in between steps: <paramref name="row"/> lifted by 16 blocks.</summary>
  public static BlockPos ParkCell(BlockPos row) =>
    new(row.X, row.Y + ParkHeight, row.Z);

  /// <summary>Builds the player at the park cell above <paramref name="row"/> and runs its checks
  /// (scenarios.md, The scenario player), leaving it parked.</summary>
  /// <param name="server">The running server, from which the engine's internal system list and
  /// player maps are read.</param>
  /// <param name="row">The block the default spawn stands on.</param>
  /// <param name="failure">Null when every check holds; otherwise the first check's failure, as
  /// <c>&lt;check&gt;: read &lt;value&gt;</c> or <c>&lt;check&gt;: threw &lt;type&gt;: &lt;message&gt;</c>.</param>
  /// <returns>The player, which must be <see cref="Stop"/>ped; null when a member resolved by name is
  /// missing or the build threw, after which the player's entries are removed from the server's maps.
  /// A player whose check failed is returned unparked.</returns>
  public static ScenarioPlayer? Start(
    ServerMain server,
    BlockPos row,
    out string? failure
  ) {
    if (
      !PlayerCheck.Read(
        "ServerMain.Systems",
        () =>
          typeof(ServerMain).GetField(
            "Systems",
            BindingFlags.Instance | BindingFlags.NonPublic
          ),
        f => f != null,
        out FieldInfo? systems,
        out failure
      )
      || !PlayerCheck.Read(
        "Vintagestory.Server.ServerSystemInventory",
        () =>
          typeof(ServerMain).Assembly.GetType(
            "Vintagestory.Server.ServerSystemInventory"
          ),
        t => t != null,
        out Type? inventory,
        out failure
      )
      || !PlayerCheck.Read(
        "ServerWorldPlayerData.connected",
        () =>
          typeof(ServerWorldPlayerData).GetField(
            "connected",
            BindingFlags.Instance | BindingFlags.NonPublic
          ),
        f => f != null,
        out FieldInfo? connected,
        out failure
      )
    )
      return null;
    ScenarioPlayer? made = PlayerCheck.Assemble(
      () => Build(server, row, systems!, inventory!, connected!),
      () =>
        Forget(
          Uid,
          server.PlayersByUid,
          server.PlayerDataManager.WorldDataByUID,
          server.PlayerDataManager.PlayerDataByUid
        ),
      out failure
    );
    if (made == null)
      return null;
    BlockPos at = made._park;
    _ =
      PlayerCheck.Read(
        "PlayerByUid",
        () => ((IWorldAccessor)server).PlayerByUid(Uid),
        p => p == made.Player,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "Entity.Player",
        () => made._data.EntityPlayer.Player,
        p => p == made.Player,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "ActiveHotbarSlot",
        () => made.Player.InventoryManager.ActiveHotbarSlot,
        s => s != null,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "GameMode",
        () => {
          ((IWorldPlayerData)made._data).CurrentGameMode =
            EnumGameMode.Survival;
          return ((IWorldPlayerData)made._data).CurrentGameMode;
        },
        m => m == EnumGameMode.Survival,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "BuildAccess",
        () =>
          server.WorldMap.TestBlockAccess(
            made.Player,
            new BlockSelection { Position = at.Copy() },
            EnumBlockAccessFlags.BuildOrBreak
          ),
        r => r == EnumWorldAccessResponse.Granted,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "IsInInteractionRangeOf",
        () =>
          ((IPlayer)made.Player).IsInInteractionRangeOf(
            new BlockPos(at.X, at.Y - 1, at.Z)
          ),
        held => held,
        out _,
        out failure
      )
      && PlayerCheck.Read(
        "Park",
        () => {
          made.Park();
          return true;
        },
        held => held,
        out _,
        out failure
      );
    return made;
  }

  /// <summary>Puts the player in creative mode with free movement at the park cell, where it neither
  /// falls, goes hungry nor picks up a drop.</summary>
  public void Park() {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Creative;
    _data.FreeMove = true;
    MoveTo(_park);
  }

  /// <summary>Empties the hotbar, off hand, backpack and mouse slot and makes the first hotbar slot the active
  /// one, so the next case starts with the hands a new player has.</summary>
  public void Empty() {
    IPlayerInventoryManager manager = Player.InventoryManager;
    IEnumerable<ItemSlot> slots = manager
      .GetHotbarInventory()
      .Concat([manager.MouseItemSlot]);
    if (
      manager.GetOwnInventory(GlobalConstants.backpackInvClassName) is { } pack
    )
      slots = slots.Concat(pack);
    EmptySlots(slots);
    Player.ActiveSlot = 0;
  }

  /// <summary>Takes the stack out of each of <paramref name="slots"/>.</summary>
  /// <param name="slots">The slots to empty; an empty slot is left as it is.</param>
  internal static void EmptySlots(IEnumerable<ItemSlot> slots) {
    foreach (ItemSlot slot in slots)
      slot.Itemstack = null;
  }

  /// <summary>Puts the player's feet in the middle of the cell <paramref name="at"/> in survival mode, on the
  /// ground, where it stays until a verb or <see cref="Park"/> moves it. The server runs no physics for it, so
  /// a block's collision hook never fires; a block entity's own tick that looks for the standing player still reaches it.</summary>
  /// <param name="at">The absolute cell.</param>
  public void StandAt(BlockPos at) {
    Survive();
    MoveTo(at);
    _data.EntityPlayer.OnGround = true;
  }

  /// <summary>Revives the player if it died and sets its health to its maximum, so the next case starts
  /// unhurt whatever a hot holder or a fall did to it.</summary>
  public void Restore() {
    EntityPlayer entity = _data.EntityPlayer;
    if (!entity.Alive)
      entity.Revive();
    if (entity.GetBehavior<EntityBehaviorHealth>() is { } health)
      health.Health = health.MaxHealth;
  }

  /// <summary>Puts the player in survival mode without free movement, where it stays until it is parked.</summary>
  public void Survive() {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
  }

  /// <summary>The cell the player stands in to place into <paramref name="at"/>: two blocks south
  /// of it, on its level.</summary>
  public static Vec3d Stand(BlockPos at) => new(at.X + 0.5, at.Y, at.Z + 2.5);

  /// <summary>The point, relative to the clicked block, where a click on its <paramref name="face"/>
  /// lands: the middle of that face.</summary>
  public static Vec3d Hit(BlockFacing face) =>
    new(
      0.5 + 0.5 * face.Normali.X,
      0.5 + 0.5 * face.Normali.Y,
      0.5 + 0.5 * face.Normali.Z
    );

  /// <summary>The yaw and pitch, in radians, that turn a player at <paramref name="eye"/> toward
  /// <paramref name="target"/> in the engine's view convention (yaw 0 looks along -Z).</summary>
  public static (float Yaw, float Pitch) Look(Vec3d eye, Vec3d target) {
    double dx = target.X - eye.X;
    double dy = target.Y - eye.Y;
    double dz = target.Z - eye.Z;
    return (
      (float)Math.Atan2(-dx, -dz),
      (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz))
    );
  }

  /// <summary>Places <paramref name="block"/> into <paramref name="at"/> the way a player's click on
  /// the <paramref name="face"/> of the block beside it does (<c>Block.TryPlaceBlock</c> with the
  /// active slot's stack, then the did-place event): moved into place in survival mode, the slot filled,
  /// then emptied and the player parked again.</summary>
  /// <param name="block">The block to hold and place.</param>
  /// <param name="at">The absolute cell the block goes into.</param>
  /// <param name="face">The face of the neighbouring block the click lands on.</param>
  /// <param name="failure">Null when the block was placed; otherwise the failure code the block gave.</param>
  /// <returns>Whether the block went down.</returns>
  public bool Place(
    Block block,
    BlockPos at,
    BlockFacing face,
    out string? failure
  ) {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
    Vec3d hit = Hit(face);
    Vec3i n = face.Normali;
    Aim(
      at,
      new Vec3d(at.X - n.X + hit.X, at.Y - n.Y + hit.Y, at.Z - n.Z + hit.Z)
    );
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    slot.Itemstack = new ItemStack(block);
    try {
      var selection = new BlockSelection {
        Position = at.Copy(),
        Face = face,
        HitPosition = hit,
        DidOffset = true,
      };
      int before = _server.BlockAccessor.GetBlock(at).Id;
      string code = "";
      if (
        !block.TryPlaceBlock(
          _server,
          Player,
          slot.Itemstack,
          selection,
          ref code
        )
      ) {
        failure = code == "" ? "refused" : code;
        return false;
      }
      _server.TriggerNeighbourBlocksUpdate(at);
      _server.EventManager.TriggerDidPlaceBlock(
        Player,
        before,
        selection,
        slot.Itemstack
      );
      failure = null;
      return true;
    } finally {
      slot.Itemstack = null;
      Park();
    }
  }

  /// <summary>The refusal a held tool gets from a block that needs a better one, or null when the tool
  /// meets the block's tier.</summary>
  /// <param name="required">The block's required mining tier; 0 or less needs no tool.</param>
  /// <param name="held">The held collectible's tool tier, 0 for an empty hand.</param>
  public static string? TierRefusal(int required, int held) =>
    required > 0 && held < required
      ? $"the block needs tool tier {required}, the hand holds tier {held}"
      : null;

  /// <summary>The refusal the block at <paramref name="at"/> gives the tool in <paramref name="slot"/>,
  /// read from the block's required mining tier and the held collectible's tool tier.</summary>
  /// <param name="block">The block being broken.</param>
  /// <param name="world">The world the block stands in.</param>
  /// <param name="at">The absolute cell of the block.</param>
  /// <param name="slot">The slot the tool sits in; an empty slot is an empty hand, tier 0.</param>
  /// <returns>Null when the tool meets the block's tier.</returns>
  public static string? TierGate(
    Block block,
    IWorldAccessor world,
    BlockPos at,
    ItemSlot slot
  ) =>
    TierRefusal(
      block.GetRequiredMiningTier(world, at),
      slot.Itemstack?.Collectible.GetToolTier(slot) ?? 0
    );

  /// <summary>Breaks the block at <paramref name="at"/> the way a player's break does
  /// (<c>ServerSystemBlockSimulation.TryModifyBlockInWorld</c>, break branch): in survival holding
  /// <paramref name="stack"/>, past the mining-tier check, through the server's break event, then
  /// <c>Collectible.OnBlockBrokenWith</c> for a held stack or <c>Block.OnBlockBroken</c> for an empty
  /// hand, then the neighbour update and the did-break event, which the server's anti-abuse monitor
  /// counts against a player it has an entry for (added here when its tick has dropped it). The slot
  /// is emptied and the player parked after.</summary>
  /// <param name="at">The absolute cell of the block.</param>
  /// <param name="stack">What the hand holds, or null for an empty hand.</param>
  /// <param name="sneak">Whether the player holds sneak for the break; released after it.</param>
  /// <param name="declare">Reads the drops the block declares; called once with the player after it is
  /// in survival mode with <paramref name="stack"/> in hand, before the break.</param>
  /// <param name="declared">What <paramref name="declare"/> returned; empty when the tier check refused.</param>
  /// <param name="failure">Null when the break ran; otherwise the tier refusal.</param>
  /// <returns>Whether the break ran.</returns>
  public bool Break(
    BlockPos at,
    ItemStack? stack,
    bool sneak,
    System.Func<IPlayer, List<ItemStack>> declare,
    out List<ItemStack> declared,
    out string? failure
  ) {
    declared = [];
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
    BlockFacing face = BlockFacing.UP;
    Vec3d hit = Hit(face);
    Aim(at, new Vec3d(at.X + hit.X, at.Y + hit.Y, at.Z + hit.Z));
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    slot.Itemstack = stack;
    try {
      Block block = _server.BlockAccessor.GetBlock(at);
      failure = TierGate(block, _server, at, slot);
      if (failure != null)
        return false;
      declared = declare(Player);
      Hold(Player.Entity.Controls, sneak ? ["sneak"] : []);
      var selection = new BlockSelection {
        Position = at.Copy(),
        Face = face,
        HitPosition = hit,
        Block = block,
      };
      float dropQuantity = 1f;
      EnumHandling handling = EnumHandling.PassThrough;
      _server.EventManager.TriggerBreakBlock(
        Player,
        selection,
        ref dropQuantity,
        ref handling
      );
      if (handling == EnumHandling.PassThrough) {
        if (stack != null)
          stack.Collectible.OnBlockBrokenWith(
            _server,
            Player.Entity,
            slot,
            selection,
            dropQuantity
          );
        else
          block.OnBlockBroken(_server, at, Player, dropQuantity);
      }
      _server.TriggerNeighbourBlocksUpdate(at);
      _abuse?.OnPlayerJoin(Player);
      _server.EventManager.TriggerDidBreakBlock(Player, block.Id, selection);
      return true;
    } finally {
      Hold(Player.Entity.Controls, []);
      slot.Itemstack = null;
      Park();
    }
  }

  /// <summary>Turns the block at <paramref name="at"/> one step with the wrench, through
  /// <c>IWrenchOrientable.Rotate</c> as the game's wrench calls it, the player as the turning
  /// entity.</summary>
  /// <param name="at">The absolute cell of the block.</param>
  /// <param name="dir">1 for the next orientation of the block's cycle, -1 for the one before.</param>
  /// <returns>Whether the block answers a wrench at all.</returns>
  public bool Wrench(BlockPos at, int dir) {
    if (
      _server
        .BlockAccessor.GetBlock(at)
        .GetInterface<IWrenchOrientable>(_server, at)
      is not { } turnable
    )
      return false;
    turnable.Rotate(
      Player.Entity,
      new BlockSelection {
        Position = at.Copy(),
        Face = BlockFacing.UP,
        HitPosition = Hit(BlockFacing.UP),
      },
      dir
    );
    return true;
  }

  /// <summary>Stands the player in reach of the block at <paramref name="at"/>, facing its
  /// <paramref name="face"/>, without a click.</summary>
  /// <param name="at">The absolute cell of the block.</param>
  /// <param name="face">The face the player looks at.</param>
  public void Face(BlockPos at, BlockFacing face) {
    Vec3d hit = Hit(face);
    Aim(at, new Vec3d(at.X + hit.X, at.Y + hit.Y, at.Z + hit.Z));
  }

  /// <summary>Holds <paramref name="stack"/> in the active slot and <paramref name="keys"/>, stands the
  /// player facing <paramref name="selection"/>'s hit on the block at <paramref name="at"/>, and
  /// right-clicks it. The did-use event fires right after <c>Block.OnBlockInteractStart</c> whatever it
  /// answered, as the server does on the start of a block use. With sneak held, a block in hand is placed
  /// first as the client does: the neighbours and the did-place event are told, and the block is not
  /// reached.</summary>
  /// <param name="at">The absolute cell of the block used.</param>
  /// <param name="selection">What the click lands on, as <see cref="InteractStep.SelectionFor"/> builds it;
  /// the hold's later calls pass it to the block.</param>
  /// <param name="stack">What the hand holds, or null for an empty hand.</param>
  /// <param name="keys">Names read by <see cref="Hold"/>.</param>
  /// <param name="offHand">What the off hand holds, or null for an empty one.</param>
  /// <returns>What <c>Block.OnBlockInteractStart</c> answered; false, with <see cref="Placed"/> set, when
  /// a block was placed instead.</returns>
  public bool UseStart(
    BlockPos at,
    BlockSelection selection,
    ItemStack? stack,
    IReadOnlyList<string> keys,
    ItemStack? offHand = null
  ) => Start(at, selection, stack, keys, offHand);

  private bool Start(
    BlockPos at,
    BlockSelection selection,
    ItemStack? stack,
    IReadOnlyList<string> keys,
    ItemStack? offHand
  ) {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
    Vec3d hit = selection.HitPosition;
    Aim(at, new Vec3d(at.X + hit.X, at.Y + hit.Y, at.Z + hit.Z));
    Hold(Player.Entity.Controls, keys);
    Player.InventoryManager.ActiveHotbarSlot.Itemstack = stack;
    Player.InventoryManager.OffhandHotbarSlot.Itemstack = offHand;
    int before = _server.BlockAccessor.GetBlock(selection.Position).Id;
    if (ClientClick.PlacesFirst(_server, Player, selection, out var put)) {
      _server.TriggerNeighbourBlocksUpdate(put!.Position);
      _server.EventManager.TriggerDidPlaceBlock(Player, before, put, stack);
      Placed = true;
      return false;
    }
    Placed = false;
    bool answered = _server
      .BlockAccessor.GetBlock(at)
      .OnBlockInteractStart(_server, Player, selection);
    _server.EventManager.TriggerDidUseBlock(Player, selection);
    return answered;
  }

  /// <summary>Holds <paramref name="stack"/> in the active slot and <paramref name="keys"/>, stands the
  /// player facing <paramref name="selection"/>'s hit on the block at <paramref name="at"/>, sets
  /// <c>Controls.HandUse</c> to <see cref="EnumHandInteract.HeldItemInteract"/> until <see cref="HeldStop"/>
  /// or <see cref="Release"/>, and starts the held item's use with <c>CollectibleObject.OnHeldUseStart</c>.</summary>
  /// <param name="at">The absolute cell aimed at.</param>
  /// <param name="selection">What the use is aimed at; the hold's later calls pass it to the item.</param>
  /// <param name="stack">What the hand holds, or null for an empty hand.</param>
  /// <param name="keys">Names read by <see cref="Hold"/>.</param>
  /// <returns>What the start left in the hand handling; <see cref="EnumHandHandling.NotHandled"/> for an
  /// empty hand.</returns>
  public EnumHandHandling HeldStart(
    BlockPos at,
    BlockSelection selection,
    ItemStack? stack,
    IReadOnlyList<string> keys
  ) {
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
    Vec3d hit = selection.HitPosition;
    Aim(at, new Vec3d(at.X + hit.X, at.Y + hit.Y, at.Z + hit.Z));
    Hold(Player.Entity.Controls, keys);
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    slot.Itemstack = stack;
    EnumHandHandling handling = EnumHandHandling.NotHandled;
    if (slot.Empty)
      return handling;
    _handUse ??= Player.Entity.Controls.HandUse;
    Player.Entity.Controls.HandUse = EnumHandInteract.HeldItemInteract;
    slot.Itemstack.Collectible.OnHeldUseStart(
      slot,
      Player.Entity,
      selection,
      null,
      EnumHandInteract.HeldItemInteract,
      true,
      ref handling
    );
    return handling;
  }

  /// <summary>One held tick of the held item's use: the player is stood and aimed again, then
  /// <c>CollectibleObject.OnHeldUseStep</c> runs with the seconds held so far.</summary>
  /// <returns>Whether the item goes on with the use.</returns>
  public bool HeldStep(BlockSelection selection, double secondsHeld) {
    BlockPos at = selection.Position;
    Aim(
      at,
      new Vec3d(
        at.X + selection.HitPosition.X,
        at.Y + selection.HitPosition.Y,
        at.Z + selection.HitPosition.Z
      )
    );
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    return slot.Itemstack.Collectible.OnHeldUseStep(
        (float)secondsHeld,
        slot,
        Player.Entity,
        selection,
        null
      ) != EnumHandInteract.None;
  }

  /// <summary>Ends the held item's use with <c>CollectibleObject.OnHeldUseStop</c> and gives
  /// <c>Controls.HandUse</c> back its earlier value.</summary>
  /// <param name="secondsHeld">The seconds the use has run.</param>
  public void HeldStop(BlockSelection selection, double secondsHeld) {
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    slot.Itemstack.Collectible.OnHeldUseStop(
      (float)secondsHeld,
      slot,
      Player.Entity,
      selection,
      null,
      EnumHandInteract.HeldItemInteract
    );
    RestoreHandUse();
  }

  private void RestoreHandUse() {
    if (_handUse is not { } was)
      return;
    Player.Entity.Controls.HandUse = was;
    _handUse = null;
  }

  /// <summary>Fills the hotbar with <paramref name="supply"/>, stands the player facing the
  /// <paramref name="face"/> of the block at <paramref name="at"/> in survival with no key held, and
  /// right-clicks it; the did-use event fires right after <c>Block.OnBlockInteractStart</c>, and the hotbar is
  /// then emptied and the player parked.</summary>
  /// <param name="at">The absolute cell of the block built.</param>
  /// <param name="face">The face the click lands on.</param>
  /// <param name="supply">One stack per hotbar slot, from the first.</param>
  /// <param name="left">What the hotbar held after the click: each stack's code and size, or <c>empty</c>.</param>
  /// <returns>What <c>Block.OnBlockInteractStart</c> answered; the did-use event fires right after it.</returns>
  /// <exception cref="InvalidOperationException">More stacks than hotbar slots.</exception>
  public bool Construct(
    BlockPos at,
    BlockFacing face,
    IReadOnlyList<ItemStack> supply,
    out string left
  ) => Construct(at, face, supply, null, 0, out left, out _);

  /// <summary>The stack each hotbar slot holds for a construction click.</summary>
  /// <param name="slots">The hotbar's slot count.</param>
  /// <param name="hand">The active slot's index, 0 to <paramref name="slots"/> - 1.</param>
  /// <param name="supply">The stage's materials, in order.</param>
  /// <param name="tool">The stack for the active slot; null puts the first supply stack there instead.</param>
  internal static ItemStack?[] Lay(
    int slots,
    int hand,
    IReadOnlyList<ItemStack> supply,
    ItemStack? tool
  ) {
    var laid = new ItemStack?[slots];
    int next = 0;
    laid[hand] = tool ?? (supply.Count > 0 ? supply[next++] : null);
    for (int i = 0; i < slots; i++)
      if (i != hand)
        laid[i] = next < supply.Count ? supply[next++] : null;
    return laid;
  }

  /// <summary>As the plain <see cref="Construct(BlockPos, BlockFacing, IReadOnlyList{ItemStack}, out string)"/>,
  /// with <paramref name="tool"/> in the active slot and the supply in the other slots, or the first
  /// supply stack in the active slot when there is no tool; when the click
  /// begins a hold, one step at <paramref name="seconds"/> and the stop follow it.</summary>
  /// <param name="tool">The stack the main hand holds, or null for an empty hand.</param>
  /// <param name="seconds">The seconds the hold runs when the click begins one; 0 for a plain click.</param>
  /// <param name="toolLeft">What the active slot held after the click: the tool as worn, or null when it
  /// broke or there was none.</param>
  /// <exception cref="InvalidOperationException">More stacks than hotbar slots besides the active one.</exception>
  public bool Construct(
    BlockPos at,
    BlockFacing face,
    IReadOnlyList<ItemStack> supply,
    ItemStack? tool,
    double seconds,
    out string left,
    out ItemStack? toolLeft
  ) {
    IInventory hotbar = Player.InventoryManager.GetHotbarInventory();
    ItemSlot hand = Player.InventoryManager.ActiveHotbarSlot;
    if (supply.Count > hotbar.Count - (tool == null ? 0 : 1))
      throw new InvalidOperationException(
        $"a stage asks for {supply.Count} stacks and the hotbar has {hotbar.Count} slots."
      );
    ((IWorldPlayerData)_data).CurrentGameMode = EnumGameMode.Survival;
    _data.FreeMove = false;
    Vec3d hit = Hit(face);
    Aim(at, new Vec3d(at.X + hit.X, at.Y + hit.Y, at.Z + hit.Z));
    Hold(Player.Entity.Controls, []);
    try {
      int handIndex = 0;
      for (int i = 0; i < hotbar.Count; i++)
        if (hotbar[i] == hand)
          handIndex = i;
      ItemStack?[] laid = Lay(hotbar.Count, handIndex, supply, tool);
      for (int i = 0; i < hotbar.Count; i++)
        hotbar[i].Itemstack = laid[i];
      var selection = new BlockSelection {
        Position = at.Copy(),
        Face = face,
        HitPosition = hit,
      };
      Block block = _server.BlockAccessor.GetBlock(at);
      bool answered = block.OnBlockInteractStart(_server, Player, selection);
      _server.EventManager.TriggerDidUseBlock(Player, selection);
      if (answered && seconds > 0) {
        block.OnBlockInteractStep((float)seconds, _server, Player, selection);
        block.OnBlockInteractStop((float)seconds, _server, Player, selection);
      }
      string[] held =
      [
        .. hotbar
          .Where(s => !s.Empty)
          .Select(s => $"{s.Itemstack.Collectible.Code} x {s.StackSize}"),
      ];
      left = held.Length == 0 ? "empty" : string.Join(", ", held);
      toolLeft = hand.Itemstack;
      return answered;
    } finally {
      foreach (ItemSlot slot in hotbar)
        slot.Itemstack = null;
      Park();
    }
  }

  /// <summary>One held tick of the interaction: the player is stood and aimed again, then
  /// <c>Block.OnBlockInteractStep</c> runs with the seconds held so far.</summary>
  /// <returns>What the block answered; false when the block stops the hold.</returns>
  public bool UseStep(BlockSelection selection, double secondsHeld) {
    BlockPos at = selection.Position;
    Aim(
      at,
      new Vec3d(
        at.X + selection.HitPosition.X,
        at.Y + selection.HitPosition.Y,
        at.Z + selection.HitPosition.Z
      )
    );
    return _server
      .BlockAccessor.GetBlock(at)
      .OnBlockInteractStep((float)secondsHeld, _server, Player, selection);
  }

  /// <summary>Makes hotbar slot <paramref name="slot"/> the active one as the server's hotbar-slot packet
  /// does: the before event may veto it, then the slot is set, a stack too hot to hold is dropped, and
  /// the after event fires.</summary>
  /// <returns>Whether the change went ahead.</returns>
  public bool Select(int slot) {
    int from = Player.ActiveSlot;
    if (
      !_server.EventManager.TriggerBeforeActiveSlotChanged(Player, from, slot)
    )
      return false;
    Player.ActiveSlot = slot;
    Player.InventoryManager.ActiveHotbarSlot.Inventory.DropSlotIfHot(
      Player.InventoryManager.ActiveHotbarSlot,
      Player
    );
    _server.EventManager.TriggerAfterActiveSlotChanged(Player, from, slot);
    return true;
  }

  /// <summary>Ends the interaction with <c>Block.OnBlockInteractStop</c>.</summary>
  /// <param name="secondsHeld">The seconds the hold has run.</param>
  public void UseStop(BlockSelection selection, double secondsHeld) {
    _server
      .BlockAccessor.GetBlock(selection.Position)
      .OnBlockInteractStop((float)secondsHeld, _server, Player, selection);
  }

  /// <summary>What the active slot holds, as <see cref="StackText"/> prints it.</summary>
  public string HandText() =>
    StackText(Player.InventoryManager.ActiveHotbarSlot.Itemstack);

  /// <summary>A stack's code and size, with the litres and code of a liquid container's content.</summary>
  /// <param name="held">The stack, or null for an empty hand.</param>
  /// <returns>The text, or <c>empty hand</c> for null.</returns>
  internal static string StackText(ItemStack? held) {
    if (held == null)
      return "empty hand";
    string text = $"{held.Collectible.Code} x {held.StackSize}";
    if (
      held.Collectible is BlockLiquidContainerBase vessel
      && vessel.GetContent(held) is { } content
    )
      return $"{text} ({vessel.GetCurrentLitres(held)} L {content.Collectible.Code})";
    return text;
  }

  /// <summary>Puts <paramref name="stack"/> in the off-hand slot, or empties it for null.</summary>
  public void HoldOffhand(ItemStack? stack) =>
    Player.Entity.LeftHandItemSlot.Itemstack = stack;

  /// <summary>What the off-hand slot holds, or null.</summary>
  public ItemStack? Offhand => Player.Entity.LeftHandItemSlot.Itemstack;

  /// <summary>The stack the active slot holds after a step.</summary>
  /// <param name="held">What the slot holds.</param>
  /// <param name="keep">Whether the step leaves the hand as it ends.</param>
  public static ItemStack? HandAfter(ItemStack? held, bool keep) =>
    keep ? held : null;

  /// <summary>Marks the stack now in the active slot as part of a filled inventory, so <see cref="Release"/>
  /// leaves it there.</summary>
  public void KeepHand() =>
    _filledHand = Player.InventoryManager.ActiveHotbarSlot.Itemstack;

  /// <summary>Releases the keys, restores <c>Controls.HandUse</c> after a held use, parks the player, empties the off hand and empties the active slot unless
  /// <paramref name="keep"/> or the slot still holds the stack <see cref="KeepHand"/> marked.</summary>
  /// <param name="keep">Whether the active slot keeps its stack for the steps that follow.</param>
  public void Release(bool keep = false) {
    ItemSlot slot = Player.InventoryManager.ActiveHotbarSlot;
    slot.Itemstack = HandAfter(
      slot.Itemstack,
      keep || ReferenceEquals(slot.Itemstack, _filledHand)
    );
    Player.InventoryManager.OffhandHotbarSlot.Itemstack = null;
    Hold(Player.Entity.Controls, []);
    RestoreHandUse();
    Park();
  }

  /// <summary>Sets the sneak (and shift), ctrl (and sprint) and sprint controls to the names in <paramref name="keys"/> and
  /// clears the others.</summary>
  /// <param name="keys">Any of <c>sneak</c>, <c>ctrl</c> and <c>sprint</c>.</param>
  internal static void Hold(EntityControls controls, IReadOnlyList<string> keys) {
    controls.Sneak = controls.ShiftKey = keys.Contains("sneak");
    controls.CtrlKey = keys.Contains("ctrl");
    controls.Sprint = controls.CtrlKey || keys.Contains("sprint");
  }

  /// <summary>Despawns the player and removes it from the server's player maps.</summary>
  public void Stop() {
    _server.EventManager.DidUseBlock -= CountUse;
    _server.EventManager.DidBreakBlock -= CountBreak;
    _server.EventManager.AfterActiveSlotChanged -= CountSlotChange;
    _server.DespawnEntity(
      _data.EntityPlayer,
      new EntityDespawnData { Reason = EnumDespawnReason.Removed }
    );
    Forget(
      Uid,
      _server.PlayersByUid,
      _server.PlayerDataManager.WorldDataByUID,
      _server.PlayerDataManager.PlayerDataByUid
    );
  }

  /// <summary>Removes <paramref name="uid"/> from each of the maps.</summary>
  internal static void Forget(string uid, params IDictionary[] maps) {
    foreach (IDictionary map in maps)
      map.Remove(uid);
  }

  private static ScenarioPlayer Build(
    ServerMain server,
    BlockPos row,
    FieldInfo systems,
    Type inventory,
    FieldInfo connected
  ) {
    BlockPos park = ParkCell(row);
    ServerWorldPlayerData data = ServerWorldPlayerData.CreateNew(Name, Uid);
    data.Init(server);
    connected.SetValue(data, true);
    data.EntityPlayer.Code = server
      .GetEntityType(GlobalConstants.EntityPlayerTypeCode)
      .Code;
    data.EntityPlayer.WatchedAttributes.SetString("playerUID", Uid);
    var player = new ServerPlayer(server, data);
    server.PlayerDataManager.WorldDataByUID[Uid] = data;
    server.PlayersByUid[Uid] = player;
    object join = ((Array)systems.GetValue(server)!)
      .Cast<object>()
      .Single(s => s.GetType() == inventory);
    inventory
      .GetMethod("OnPlayerJoin", [typeof(ServerPlayer)])!
      .Invoke(join, [player]);
    var made = new ScenarioPlayer(
      server,
      data,
      player,
      park,
      ((Array)systems.GetValue(server)!)
        .OfType<PlayerAntiAbuseMonitor>()
        .SingleOrDefault()
    );
    made.MoveTo(park);
    server.SpawnEntity(data.EntityPlayer);
    return made;
  }

  private void Aim(BlockPos at, Vec3d target) {
    Vec3d stand = Stand(at);
    MoveTo(stand);
    Vec3d eye = stand.AddCopy(_data.EntityPlayer.LocalEyePos);
    (_data.EntityPlayer.ServerPos.Yaw, _data.EntityPlayer.ServerPos.Pitch) =
      Look(eye, target);
    _data.EntityPlayer.Pos.SetFrom(_data.EntityPlayer.ServerPos);
  }

  private void MoveTo(BlockPos at) =>
    MoveTo(new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5));

  private void MoveTo(Vec3d at) {
    _data.EntityPlayer.ServerPos.SetPos(at.X, at.Y, at.Z);
    _data.EntityPlayer.Pos.SetFrom(_data.EntityPlayer.ServerPos);
  }
}
