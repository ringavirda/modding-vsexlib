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
public sealed class TestPlayer {
  private readonly DummySlot _activeSlot;

  private TestPlayer(
    IPlayer player,
    IServerPlayer? serverPlayer,
    EntityPlayer entity,
    DummySlot activeSlot
  ) {
    Player = player;
    ServerPlayer = serverPlayer;
    Entity = entity;
    _activeSlot = activeSlot;
  }

  /// <summary>The substituted player, valid on every lane.</summary>
  public IPlayer Player { get; }

  /// <summary>The same object as <see cref="Player"/>, viewed as <see cref="IServerPlayer"/>, or
  /// <c>null</c> when this lane's game assembly cannot proxy it.</summary>
  public IServerPlayer? ServerPlayer { get; }

  /// <summary>The player's active hotbar slot - a real <see cref="ItemSlot"/>, not a fake.</summary>
  public ItemSlot ActiveSlot => _activeSlot;

  /// <summary>The substituted entity behind <see cref="Player"/>; its <c>Controls</c> field is the
  /// genuine <see cref="EntityPlayer"/> initialiser, not a substitute.</summary>
  public EntityPlayer Entity { get; }

  /// <summary>Whether the player is sneaking; backed by <see cref="Entity"/>'s own controls.</summary>
  public bool Sneaking {
    get => Entity.Controls.Sneak;
    set => Entity.Controls.Sneak = value;
  }

  /// <summary>Puts <paramref name="stack"/> in the active hotbar slot, or empties it for <c>null</c>.</summary>
  public void Hold(ItemStack? stack) => _activeSlot.Itemstack = stack;

  /// <summary>
  /// Builds a player standing in <paramref name="world"/>, as a substituted
  /// <see cref="IServerPlayer"/> or, failing that, a plain <see cref="IPlayer"/>.
  /// </summary>
  public static TestPlayer Create(
    TestWorld world,
    string uid = "test",
    string name = "Tester"
  ) {
    EntityPlayer entity = Substitute.For<EntityPlayer>();
    entity.World = world.World;

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

    var activeSlot = new DummySlot();
    var inventoryManager = Substitute.For<IPlayerInventoryManager>();
    inventoryManager.ActiveHotbarSlot.Returns(activeSlot);
    player.InventoryManager.Returns(inventoryManager);

    return new TestPlayer(player, serverPlayer, entity, activeSlot);
  }
}
