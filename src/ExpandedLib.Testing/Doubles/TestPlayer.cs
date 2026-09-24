using System;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing;

/// <summary>
/// A player with a real hotbar, backed by a substituted <see cref="IPlayer"/>/
/// <see cref="EntityPlayer"/> with a real <see cref="DummySlot"/> as the active slot.
/// </summary>
/// <remarks>The world answers <see cref="IWorldAccessor.PlayerByUid"/> for this player's uid with
/// <see cref="Player"/>, so <see cref="EntityPlayer.Player"/> resolves to it, as the game's
/// right-click construction reads it.</remarks>
public sealed class TestPlayer {
  private readonly DummySlot _activeSlot;

  private TestPlayer(
    IPlayer player,
    IServerPlayer? serverPlayer,
    EntityPlayer entity,
    DummySlot activeSlot,
    InventoryGeneric hotbar
  ) {
    Player = player;
    ServerPlayer = serverPlayer;
    Entity = entity;
    _activeSlot = activeSlot;
    Hotbar = hotbar;
  }

  /// <summary>The substituted player, valid on every lane.</summary>
  public IPlayer Player { get; }

  /// <summary>The same object as <see cref="Player"/>, viewed as <see cref="IServerPlayer"/>, or
  /// <c>null</c> when this lane's game assembly cannot proxy it.</summary>
  public IServerPlayer? ServerPlayer { get; }

  /// <summary>The player's active hotbar slot - a real <see cref="ItemSlot"/>, not a fake. It is
  /// not one of <see cref="Hotbar"/>'s slots.</summary>
  public ItemSlot ActiveSlot => _activeSlot;

  /// <summary>The inventory <c>InventoryManager.GetHotbarInventory()</c> returns: a real, empty
  /// 12-slot inventory, as the game's hotbar, the last slot the off hand.</summary>
  public InventoryGeneric Hotbar { get; }

  /// <summary>The substituted entity behind <see cref="Player"/>; its <c>Controls</c> field is the
  /// genuine <see cref="EntityPlayer"/> initialiser, not a substitute.</summary>
  public EntityPlayer Entity { get; }

  /// <summary>Whether the player is sneaking; backed by <see cref="Entity"/>'s own controls.</summary>
  public bool Sneaking {
    get => Entity.Controls.Sneak;
    set => Entity.Controls.Sneak = value;
  }

  /// <summary>Whether the player holds Ctrl; backed by <see cref="Entity"/>'s own controls, where
  /// the game's creative construction reads it.</summary>
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
    player.PlayerUID.Returns(uid);
    player.PlayerName.Returns(name);
    world.World.PlayerByUid(uid).Returns(player);

    var activeSlot = new DummySlot();
    InventoryGeneric hotbar = TestInventory.Of(world, 12, $"hotbar-{uid}");
    var inventoryManager = Substitute.For<IPlayerInventoryManager>();
    inventoryManager.ActiveHotbarSlot.Returns(activeSlot);
    inventoryManager.GetHotbarInventory().Returns(hotbar);
    player.InventoryManager.Returns(inventoryManager);

    return new TestPlayer(player, serverPlayer, entity, activeSlot, hotbar);
  }
}
