using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing;

/// <summary>
/// A player with a real hotbar, backed by a substituted <see cref="IPlayer"/>/
/// <see cref="EntityPlayer"/> with a real <see cref="DummySlot"/> as the active slot.
/// </summary>
/// <remarks>The world answers <see cref="IWorldAccessor.PlayerByUid"/> for this player's uid with
/// <see cref="Player"/>, so <see cref="EntityPlayer.Player"/> resolves to it, as the game's
/// right-click construction reads it. Its inventory manager holds <see cref="Hotbar"/> and
/// <see cref="Mouse"/> under the engine's ids (<c>hotbar-</c> and <c>mouse-</c> with the uid), and
/// <c>OpenInventory</c>/<c>CloseInventory</c> add and drop an inventory there and run its own
/// <c>Open</c>/<c>Close</c>, as the server's does, so the engine's slot protocol finds the cursor
/// and the opened inventories.</remarks>
public sealed class TestPlayer {
  /// <summary>The live scenario runner's tick, the step of a held interaction, in seconds.</summary>
  public const float InteractStepSeconds = 0.1f;

  private const int OffHandIndex = 11;

  /// <summary>The hotbar slots <see cref="Select"/> chooses among, numbered from 0.</summary>
  public const int HotbarSlots = 10;

  private readonly TestWorld _world;
  private readonly DummySlot _activeSlot;
  private int _activeNumber;

  private TestPlayer(
    TestWorld world,
    IPlayer player,
    IServerPlayer? serverPlayer,
    EntityPlayer entity,
    DummySlot activeSlot,
    InventoryGeneric hotbar,
    InventoryGeneric mouse
  ) {
    _world = world;
    Player = player;
    ServerPlayer = serverPlayer;
    Entity = entity;
    _activeSlot = activeSlot;
    Hotbar = hotbar;
    Mouse = mouse;
  }

  /// <summary>The substituted player, valid on every lane.</summary>
  public IPlayer Player { get; }

  /// <summary>The same object as <see cref="Player"/>, viewed as <see cref="IServerPlayer"/>, or
  /// <c>null</c> when this lane's game assembly cannot proxy it.</summary>
  public IServerPlayer? ServerPlayer { get; }

  /// <summary>The player's active hotbar slot - a real <see cref="ItemSlot"/>, not a fake, and
  /// the slot <see cref="Entity"/>'s <c>RightHandItemSlot</c> answers. It is not one of
  /// <see cref="Hotbar"/>'s slots.</summary>
  public ItemSlot ActiveSlot => _activeSlot;

  /// <summary>The stack hotbar slot <paramref name="index"/> holds, numbered as <see cref="Select"/> numbers
  /// them. The active slot, <see cref="ActiveNumber"/>, reads <see cref="ActiveSlot"/>; the others read
  /// <see cref="Hotbar"/>'s.</summary>
  /// <param name="index">The hotbar index, from 0 to 11.</param>
  /// <returns>The stack, or null for an empty slot.</returns>
  public ItemStack? StackInSlot(int index) =>
    index == _activeNumber ? _activeSlot.Itemstack : Hotbar[index].Itemstack;

  /// <summary>The inventory <c>InventoryManager.GetHotbarInventory()</c> returns: a real, empty
  /// 12-slot inventory, as the game's hotbar, the last slot the off hand.</summary>
  public InventoryGeneric Hotbar { get; }

  /// <summary>The player's mouse cursor: a real, empty 1-slot inventory under the id
  /// <c>mouse-</c> with the uid, the slot the engine's slot protocol moves a clicked stack
  /// through and <c>InventoryManager.MouseItemSlot</c> answers.</summary>
  public InventoryGeneric Mouse { get; }

  /// <summary>The substituted entity behind <see cref="Player"/>; its <c>Controls</c> field is the
  /// genuine <see cref="EntityPlayer"/> initialiser, not a substitute, and the same object
  /// <see cref="Player"/>'s <c>WorldData.EntityControls</c> answers, so a key set on either reads
  /// on both.</summary>
  public EntityPlayer Entity { get; }

  /// <summary>Whether the player is sneaking; backed by <see cref="Entity"/>'s own controls.</summary>
  public bool Sneaking {
    get => Entity.Controls.Sneak;
    set => Entity.Controls.Sneak = value;
  }

  /// <summary>Whether the player holds Ctrl; backed by <see cref="Entity"/>'s own controls, where
  /// exlib's creative construction reads it.</summary>
  public bool CtrlHeld {
    get => Entity.Controls.CtrlKey;
    set => Entity.Controls.CtrlKey = value;
  }

  /// <summary>The player's game mode, read through <c>Player.WorldData.CurrentGameMode</c>;
  /// <see cref="EnumGameMode.Guest"/> until set.</summary>
  public EnumGameMode GameMode {
    get => Player.WorldData.CurrentGameMode;
    set => Player.WorldData.CurrentGameMode.Returns(value);
  }

  /// <summary>Puts <paramref name="stack"/> in the active hotbar slot, or empties it for <c>null</c>.</summary>
  public void Hold(ItemStack? stack) => _activeSlot.Itemstack = stack;

  /// <summary>The player's off-hand slot, the last slot of <see cref="Hotbar"/> and the slot
  /// <see cref="Entity"/>'s <c>LeftHandItemSlot</c> answers.</summary>
  public ItemSlot OffHandSlot => Hotbar[OffHandIndex];

  /// <summary>The number of the active hotbar slot, 0 until <see cref="Select"/> changes it.</summary>
  public int ActiveNumber => _activeNumber;

  /// <summary>Makes hotbar slot <paramref name="slot"/> the active one, as the player's number keys do, and
  /// raises the server's <c>AfterActiveSlotChanged</c> once with the old and the new number.</summary>
  /// <remarks>A <c>BeforeActiveSlotChanged</c> handler answering <see cref="EnumHandling.PreventDefault"/> or
  /// <see cref="EnumHandling.PreventSubsequent"/> vetoes the change. The stack of the slot left, held in
  /// <see cref="ActiveSlot"/> or in its <see cref="Hotbar"/> slot, goes back to that slot, and the stack of
  /// <paramref name="slot"/> moves into <see cref="ActiveSlot"/>. As <c>DropSlotIfHot</c> does, a stack hotter than
  /// <c>GlobalConstants.TooHotToTouchTemperature</c> is then dropped through <c>InventoryManager.DropItem</c>,
  /// unless the player is in creative mode, the collectible allows hot crafting or the off hand holds a
  /// <c>heatResistant</c> item. <c>InventoryManager.ActiveHotbarSlotNumber</c> answers the new number. Nothing
  /// happens, and no event is raised, when <paramref name="slot"/> is already active or the change is vetoed.</remarks>
  /// <param name="slot">The hotbar slot, 0 up to <see cref="HotbarSlots"/> exclusive.</param>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a hotbar slot.</exception>
  /// <exception cref="InvalidOperationException">The slot left holds a stack in <see cref="ActiveSlot"/> and another in its <see cref="Hotbar"/> slot.</exception>
  /// <exception cref="InvalidOperationException">This lane's game assembly cannot proxy <see cref="IServerPlayer"/>, so there is no server event to raise.</exception>
  public void Select(int slot) {
    if (slot < 0 || slot >= HotbarSlots)
      throw new ArgumentOutOfRangeException(
        nameof(slot),
        slot,
        $"a hotbar slot is 0 to {HotbarSlots - 1}"
      );
    IServerPlayer server =
      ServerPlayer
      ?? throw new InvalidOperationException(
        "this lane cannot proxy IServerPlayer, so no slot-change event is raised"
      );
    int from = _activeNumber;
    if (slot == from)
      return;
    ItemStack? leaving = _activeSlot.Itemstack;
    if (
      leaving != null
      && Hotbar[from].Itemstack is { } kept
      && kept != leaving
    )
      throw new InvalidOperationException(
        $"hotbar slot {from} and the active slot both hold a stack, so one would be lost"
      );
    if (!AllowsChange(server, from, slot))
      return;
    Hotbar[from].Itemstack = leaving ?? Hotbar[from].Itemstack;
    _activeSlot.Itemstack = Hotbar[slot].Itemstack;
    Hotbar[slot].Itemstack = null;
    _activeNumber = slot;
    DropIfTooHot();
    _world.Api.Event.AfterActiveSlotChanged += Raise.Event<
      Action<IServerPlayer, ActiveSlotChangeEventArgs>
    >(server, new ActiveSlotChangeEventArgs(from, slot));
  }

  private bool AllowsChange(IServerPlayer server, int from, int to) {
    var handlers =
      new List<Vintagestory.API.Common.Func<
        IServerPlayer,
        ActiveSlotChangeEventArgs,
        EnumHandling
      >>();
    foreach (NSubstitute.Core.ICall call in _world.Api.Event.ReceivedCalls()) {
      string name = call.GetMethodInfo().Name;
      if (name == "add_BeforeActiveSlotChanged")
        handlers.Add(
          (Vintagestory.API.Common.Func<
            IServerPlayer,
            ActiveSlotChangeEventArgs,
            EnumHandling
          >)
            call.GetOriginalArguments()[0]!
        );
      else if (name == "remove_BeforeActiveSlotChanged")
        handlers.Remove(
          (Vintagestory.API.Common.Func<
            IServerPlayer,
            ActiveSlotChangeEventArgs,
            EnumHandling
          >)
            call.GetOriginalArguments()[0]!
        );
    }
    var args = new ActiveSlotChangeEventArgs(from, to);
    bool allowed = true;
    foreach (var handler in handlers)
      switch (handler(server, args)) {
        case EnumHandling.PreventSubsequent:
          return false;
        case EnumHandling.PreventDefault:
          allowed = false;
          break;
      }
    return allowed;
  }

  private bool GiveToHotbar(ItemStack stack) {
    if (stack.StackSize == 0)
      return false;
    ItemStack? held = _activeSlot.Itemstack;
    ItemSlot active = Hotbar[_activeNumber];
    if (held != null && active.Itemstack == null)
      active.Itemstack = held;
    try {
      var source = new DummySlot(stack);
      var op = new ItemStackMoveOperation(
        _world.World,
        EnumMouseButton.Left,
        0,
        EnumMergePriority.AutoMerge,
        stack.StackSize
      );
      var skipped = new List<ItemSlot>();
      bool placed = false;
      while (source.StackSize > 0) {
        ItemSlot? target = Hotbar.GetBestSuitedSlot(source, op, skipped).slot;
        if (target == null)
          break;
        skipped.Add(target);
        placed = true;
        op.RequestedQuantity = source.StackSize;
        source.TryPutInto(target, ref op);
      }
      if (source.Itemstack == null)
        stack.StackSize = 0;
      return placed;
    } finally {
      if (held != null && ReferenceEquals(active.Itemstack, held))
        active.Itemstack = null;
    }
  }

  private void DropIfTooHot() {
    ItemStack? stack = _activeSlot.Itemstack;
    if (stack == null || GameMode == EnumGameMode.Creative)
      return;
    CollectibleObject collectible = stack.Collectible;
    if (
      collectible.Attributes?.IsTrue("allowHotCrafting") == true
      || collectible.GetTemperature(_world.World, stack)
        <= GlobalConstants.TooHotToTouchTemperature
      || OffHandSlot.Itemstack?.Collectible.Attributes?.IsTrue("heatResistant")
        == true
    )
      return;
    Player.InventoryManager.DropItem(_activeSlot, true);
    _activeSlot.Itemstack = null;
  }

  /// <summary>Puts <paramref name="stack"/> in <see cref="OffHandSlot"/>, or empties it for <c>null</c>.</summary>
  public void HoldOffHand(ItemStack? stack) => OffHandSlot.Itemstack = stack;

  /// <summary>The window of <paramref name="entity"/> as this player works it; see
  /// <see cref="TestWindow"/>.</summary>
  /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
  public TestWindow Window(BlockEntity entity) =>
    new(this, entity ?? throw new ArgumentNullException(nameof(entity)));

  /// <summary>Right-clicks the block at <paramref name="pos"/> as the server handles a block
  /// use: <see cref="Block.OnBlockInteractStart"/>, then, when it takes the click,
  /// <see cref="Block.OnBlockInteractStep"/> every <see cref="InteractStepSeconds"/> with the
  /// seconds held so far, and <see cref="Block.OnBlockInteractStop"/> once, when a step declines
  /// or the hold ends.</summary>
  /// <remarks>A spectator, or a player the claims refuse use of the cell, gets nothing. World
  /// ticks do not run during the hold; the keys last for the call only. The held collectible's
  /// interaction, the reach test and the server's use events are not run.</remarks>
  /// <param name="pos">The cell clicked.</param>
  /// <param name="held">Put in <see cref="ActiveSlot"/> first; null leaves the hand.</param>
  /// <param name="sneak">Holds <c>Sneak</c> and <c>ShiftKey</c>.</param>
  /// <param name="ctrl">Holds <c>CtrlKey</c> and <c>Sprint</c>.</param>
  /// <param name="seconds">The hold in seconds, 0 or more; 0 clicks with no step.</param>
  /// <param name="face">The face clicked, at its centre; null for <c>BlockFacing.UP</c>.</param>
  /// <returns><see cref="Block.OnBlockInteractStart"/>'s answer; false when not called.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is negative or NaN.</exception>
  public bool Interact(
    BlockPos pos,
    ItemStack? held = null,
    bool sneak = false,
    bool ctrl = false,
    float seconds = 0f,
    BlockFacing? face = null
  ) => Interact(pos, held, sneak, ctrl, false, seconds, face, null);

  internal bool Interact(
    BlockPos pos,
    ItemStack? held,
    bool sneak,
    bool ctrl,
    bool sprint,
    float seconds,
    BlockFacing? face,
    Action? eachSecond
  ) =>
    Use(
      pos,
      held,
      sneak,
      ctrl,
      sprint,
      seconds,
      FaceSelection(pos, face ?? BlockFacing.UP),
      eachSecond
    );

#if GAME_GE_1_22
  /// <summary>Right-clicks control <paramref name="control"/> of the block at <paramref name="pos"/>: as
  /// <see cref="Interact(BlockPos, ItemStack?, bool, bool, float, BlockFacing?)"/>, with the selection on the
  /// control's box, so the block reads the control's id from it.</summary>
  /// <param name="pos">The cell whose control is clicked.</param>
  /// <param name="control">The id of the control, as its box carries it.</param>
  /// <param name="held">Put in <see cref="ActiveSlot"/> first; null leaves the hand.</param>
  /// <param name="sneak">Holds <c>Sneak</c> and <c>ShiftKey</c>.</param>
  /// <param name="ctrl">Holds <c>CtrlKey</c> and <c>Sprint</c>.</param>
  /// <param name="seconds">The hold in seconds, 0 or more; 0 clicks with no step.</param>
  /// <param name="face">The side of the control's box clicked, at its centre; null for the top.</param>
  /// <returns><see cref="Block.OnBlockInteractStart"/>'s answer; false when not called.</returns>
  /// <exception cref="ArgumentException"><paramref name="control"/> is no box's id of the cell; the message
  /// names the ids the cell has.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is negative or NaN.</exception>
  public bool UseControl(
    BlockPos pos,
    string control,
    ItemStack? held = null,
    bool sneak = false,
    bool ctrl = false,
    float seconds = 0f,
    BlockFacing? face = null
  ) => UseControl(pos, control, held, sneak, ctrl, false, seconds, face, null);

  internal bool UseControl(
    BlockPos pos,
    string control,
    ItemStack? held,
    bool sneak,
    bool ctrl,
    bool sprint,
    float seconds,
    BlockFacing? face,
    Action? eachSecond
  ) =>
    Use(
      pos,
      held,
      sneak,
      ctrl,
      sprint,
      seconds,
      ControlAim.Selection(_world.Accessor, pos, control, face),
      eachSecond
    );

  /// <summary>Makes control <paramref name="control"/> of the block at <paramref name="pos"/> the player's
  /// looked-at selection, the one a block's info reads.</summary>
  /// <param name="pos">The cell whose control is looked at.</param>
  /// <param name="control">The id of the control, as its box carries it.</param>
  /// <exception cref="ArgumentException"><paramref name="control"/> is no box's id of the cell; the message
  /// names the ids the cell has.</exception>
  public void LookAt(BlockPos pos, string control)
  {
    BlockSelection selection = ControlAim.Selection(
      _world.Accessor,
      pos,
      control,
      null
    );
    Player.CurrentBlockSelection.Returns(selection);
  }

  internal void LookAway() =>
    Player.CurrentBlockSelection.Returns((BlockSelection)null!);
#endif

  private bool Use(
    BlockPos pos,
    ItemStack? held,
    bool sneak,
    bool ctrl,
    bool sprint,
    float seconds,
    BlockSelection selection,
    Action? eachSecond
  ) {
    if (!(seconds >= 0f))
      throw new ArgumentOutOfRangeException(
        nameof(seconds),
        seconds,
        "a hold lasts 0 seconds or more"
      );
    if (held != null)
      Hold(held);
    if (GameMode == EnumGameMode.Spectator)
      return false;
    if (!_world.World.Claims.TryAccess(Player, pos, EnumBlockAccessFlags.Use))
      return false;

    EntityControls controls = Entity.Controls;
    (bool, bool, bool, bool) was = (
      controls.Sneak,
      controls.ShiftKey,
      controls.CtrlKey,
      controls.Sprint
    );
    controls.Sneak = controls.ShiftKey = sneak;
    controls.CtrlKey = ctrl;
    controls.Sprint = ctrl || sprint;
    try {
      Block block = _world.Accessor.GetBlock(pos);
      if (!block.OnBlockInteractStart(_world.World, Player, selection))
        return false;
      float used = HoldSteps(
        seconds,
        s => block.OnBlockInteractStep(s, _world.World, Player, selection),
        eachSecond
      );
      block.OnBlockInteractStop(used, _world.World, Player, selection);
      return true;
    } finally {
      (controls.Sneak, controls.ShiftKey, controls.CtrlKey, controls.Sprint) =
        was;
    }
  }

  private static BlockSelection FaceSelection(BlockPos pos, BlockFacing side) =>
    new() {
      Position = pos.Copy(),
      Face = side,
      HitPosition = new Vec3d(
        0.5 + 0.5 * side.Normali.X,
        0.5 + 0.5 * side.Normali.Y,
        0.5 + 0.5 * side.Normali.Z
      ),
    };

  internal static float HoldSteps(
    float seconds,
    System.Func<float, bool> step,
    Action? eachSecond
  ) {
    int perSecond = (int)Math.Round(1f / InteractStepSeconds);
    int steps = (int)Math.Floor(seconds / InteractStepSeconds + 1e-4);
    for (int i = 1; i <= steps; i++) {
      float used = i * InteractStepSeconds;
      if (!step(used))
        return used;
      if (eachSecond != null && i % perSecond == 0)
        eachSecond();
    }
    return seconds;
  }

  /// <summary>
  /// Builds a player standing in <paramref name="world"/>, as a substituted
  /// <see cref="IServerPlayer"/> or, failing that, a plain <see cref="IPlayer"/>.
  /// </summary>
  /// <remarks>Makes <paramref name="world"/> answer <see cref="IWorldAccessor.PlayerByUid"/> for
  /// <paramref name="uid"/> with the new player, replacing an earlier player of that uid. The
  /// entity's <c>Api</c> is the world's, as a recipe's <c>Matches</c> reads it.</remarks>
  public static TestPlayer Create(
    TestWorld world,
    string uid = "test",
    string name = "Tester"
  ) {
    EntityPlayer entity = Substitute.For<EntityPlayer>();
    entity.World = world.World;
    entity.Api = world.Api;
    entity.WatchedAttributes.SetString("playerUID", uid);

    var activeSlot = new DummySlot();
    IPlayer player;
    IServerPlayer? serverPlayer;
    try {
      serverPlayer = Substitute.For<IServerPlayer>();
      player = serverPlayer;
    } catch (TypeLoadException) {
      serverPlayer = null;
      player = Substitute.For<IPlayer>();
    }

    player.Entity.Returns(entity);
    entity.RightHandItemSlot.Returns(activeSlot);
    player.PlayerUID.Returns(uid);
    player.PlayerName.Returns(name);
    player.WorldData.PlayerUID.Returns(uid);
    player.WorldData.EntityControls.Returns(entity.Controls);
    world.AddPlayer(uid, player);

    InventoryGeneric hotbar = TestInventory.Of(world, 12, $"hotbar-{uid}");
    entity.LeftHandItemSlot.Returns(hotbar[OffHandIndex]);
    InventoryGeneric mouse = TestInventory.Of(world, 1, $"mouse-{uid}");
    IPlayerInventoryManager manager = Inventories(
      player,
      activeSlot,
      hotbar,
      mouse
    );
    player.InventoryManager.Returns(manager);

    var made = new TestPlayer(
      world,
      player,
      serverPlayer,
      entity,
      activeSlot,
      hotbar,
      mouse
    );
    manager.ActiveHotbarSlotNumber.Returns(_ => made._activeNumber);
    manager
      .TryGiveItemstack(Arg.Any<ItemStack>(), Arg.Any<bool>())
      .Returns(ci => made.GiveToHotbar(ci.Arg<ItemStack>()));
    return made;
  }

  private static IPlayerInventoryManager Inventories(
    IPlayer player,
    DummySlot activeSlot,
    InventoryGeneric hotbar,
    InventoryGeneric mouse
  ) {
    var held = new Dictionary<string, IInventory> {
      [hotbar.InventoryID] = hotbar,
      [mouse.InventoryID] = mouse,
    };
    var manager = Substitute.For<IPlayerInventoryManager>();
    manager.ActiveHotbarSlot.Returns(activeSlot);
    manager.OffhandHotbarSlot.Returns(hotbar[OffHandIndex]);
    manager.OffhandTool.Returns(_ =>
      hotbar[OffHandIndex].Itemstack?.Collectible.GetTool(hotbar[OffHandIndex])
    );
    manager.GetHotbarInventory().Returns(hotbar);
    manager.MouseItemSlot.Returns(_ => mouse[0]);
    manager.Inventories.Returns(held);
    manager.OpenedInventories.Returns(_ =>
      held.Values.Where(i => i.HasOpened(player)).ToList()
    );
    manager
      .GetInventory(Arg.Any<string>())
      .Returns(ci => held.GetValueOrDefault(ci.Arg<string>()));
    manager
      .GetInventory(Arg.Any<string>(), out InventoryBase? _)
      .Returns(ci => {
        ci[1] = held.GetValueOrDefault(ci.ArgAt<string>(0)) as InventoryBase;
        return ci[1] != null;
      });
    manager
      .GetOwnInventory(Arg.Any<string>())
      .Returns(ci =>
        held.GetValueOrDefault($"{ci.Arg<string>()}-{player.PlayerUID}")
      );
    manager
      .HasInventory(Arg.Any<IInventory>())
      .Returns(ci => held.ContainsValue(ci.Arg<IInventory>()));
    manager
      .OpenInventory(Arg.Any<IInventory>())
      .Returns(ci => {
        IInventory inventory = ci.Arg<IInventory>();
        held[inventory.InventoryID] = inventory;
        return inventory.Open(player);
      });
    manager
      .CloseInventory(Arg.Any<IInventory>())
      .Returns(ci => {
        IInventory inventory = ci.Arg<IInventory>();
        if (inventory.RemoveOnClose)
          held.Remove(inventory.InventoryID);
        return inventory.Close(player);
      });
    return manager;
  }
}
