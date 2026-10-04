using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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

  private readonly TestWorld _world;
  private readonly DummySlot _activeSlot;

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
  /// ticks do not run during the hold; the keys, on <see cref="Entity"/>'s controls, last for the
  /// call only. The held collectible's interaction, the reach test and the server's use events are
  /// not run.</remarks>
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

    BlockFacing side = face ?? BlockFacing.UP;
    var selection = new BlockSelection {
      Position = pos.Copy(),
      Face = side,
      HitPosition = new Vec3d(
        0.5 + 0.5 * side.Normali.X,
        0.5 + 0.5 * side.Normali.Y,
        0.5 + 0.5 * side.Normali.Z
      ),
    };
    EntityControls controls = Entity.Controls;
    (bool, bool, bool, bool) was = (
      controls.Sneak,
      controls.ShiftKey,
      controls.CtrlKey,
      controls.Sprint
    );
    controls.Sneak = controls.ShiftKey = sneak;
    controls.CtrlKey = controls.Sprint = ctrl;
    try {
      Block block = _world.Accessor.GetBlock(pos);
      if (!block.OnBlockInteractStart(_world.World, Player, selection))
        return false;
      int steps = (int)Math.Floor(seconds / InteractStepSeconds + 1e-4);
      for (int i = 1; i <= steps; i++) {
        float used = i * InteractStepSeconds;
        if (!block.OnBlockInteractStep(used, _world.World, Player, selection)) {
          block.OnBlockInteractStop(used, _world.World, Player, selection);
          return true;
        }
      }
      block.OnBlockInteractStop(seconds, _world.World, Player, selection);
      return true;
    } finally {
      (controls.Sneak, controls.ShiftKey, controls.CtrlKey, controls.Sprint) =
        was;
    }
  }

  /// <summary>
  /// Builds a player standing in <paramref name="world"/>, as a substituted
  /// <see cref="IServerPlayer"/> or, failing that, a plain <see cref="IPlayer"/>.
  /// </summary>
  /// <remarks>Makes <paramref name="world"/> answer <see cref="IWorldAccessor.PlayerByUid"/> for
  /// <paramref name="uid"/> with the new player, replacing an earlier player of that uid.</remarks>
  public static TestPlayer Create(
    TestWorld world,
    string uid = "test",
    string name = "Tester"
  ) {
    EntityPlayer entity = Substitute.For<EntityPlayer>();
    entity.World = world.World;
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
    world.World.PlayerByUid(uid).Returns(player);

    InventoryGeneric hotbar = TestInventory.Of(world, 12, $"hotbar-{uid}");
    InventoryGeneric mouse = TestInventory.Of(world, 1, $"mouse-{uid}");
    IPlayerInventoryManager manager = Inventories(
      player,
      activeSlot,
      hotbar,
      mouse
    );
    player.InventoryManager.Returns(manager);

    return new TestPlayer(
      world,
      player,
      serverPlayer,
      entity,
      activeSlot,
      hotbar,
      mouse
    );
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
