using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// A block entity's window as <see cref="TestPlayer"/> works it: each verb hands the entity's
/// <see cref="BlockEntity.OnReceivedClientPacket"/> the packet the game's block-entity dialog sends,
/// as the server's block-entity packet handler does, so the entity's own open/close handshake,
/// access check and the engine's slot protocol (<c>InvNetworkUtil.HandleClientPacket</c>) run.
/// </summary>
/// <remarks>Only the server half runs: no dialog is drawn and no client inventory predicts the
/// click. The server's handler checks nothing about the player before the entity does, and the
/// engine's slot-click protocol does not ask whether the inventory is open, so a put or take sent
/// without <see cref="Open"/> lands unless the entity refuses it. The game's client-side half of a
/// put and a take, the hotbar click that moves a stack between the hand and the cursor, is done
/// directly on <see cref="TestPlayer.ActiveSlot"/> and <see cref="TestPlayer.Mouse"/>.</remarks>
public sealed class TestWindow {
  /// <summary>The engine's open packet id (<c>EnumBlockEntityPacketId.Open</c>).</summary>
  public const int OpenPacketId = 1000;

  /// <summary>The engine's close packet id (<c>EnumBlockEntityPacketId.Close</c>).</summary>
  public const int ClosePacketId = 1001;

  internal TestWindow(TestPlayer player, BlockEntity entity) {
    Player = player;
    Entity = entity;
  }

  /// <summary>The player working the window.</summary>
  public TestPlayer Player { get; }

  /// <summary>The block entity whose window this is.</summary>
  public BlockEntity Entity { get; }

  /// <summary>Sends the open packet, as the dialog does when it opens.</summary>
  public void Open() => Send(OpenPacketId, null);

  /// <summary>Sends the close packet, as the dialog does when it closes.</summary>
  public void Close() => Send(ClosePacketId, null);

  /// <summary>Sends a button's packet, as a machine's dialog does for its own controls.</summary>
  /// <param name="packetId">The entity's own packet id; above <see cref="ClosePacketId"/>.</param>
  /// <param name="data">The packet's payload, as the dialog serialises it; null for none.</param>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="packetId"/> is a slot,
  /// open or close packet id (1001 or below).</exception>
  public void Press(int packetId, byte[]? data = null) {
    if (packetId <= ClosePacketId)
      throw new ArgumentOutOfRangeException(
        nameof(packetId),
        packetId,
        "a button's packet id is above the close packet's 1001"
      );
    Send(packetId, data);
  }

  /// <summary>Puts the stack in <see cref="TestPlayer.ActiveSlot"/> into slot
  /// <paramref name="slotId"/>: moves it onto the cursor, sends the left click on that slot the
  /// dialog sends, and moves whatever the cursor holds after it back to the hand.</summary>
  /// <param name="slotId">The slot's index in the entity's inventory, from 0.</param>
  /// <returns>How many items left the hand; 0 when the entity or the slot refused them.</returns>
  /// <exception cref="InvalidOperationException">The entity is not an
  /// <see cref="IBlockEntityContainer"/> with an inventory, the hand is empty, or the cursor already
  /// holds a stack.</exception>
  public int Put(int slotId) {
    InventoryBase inventory = Container();
    ItemSlot cursor = Cursor();
    ItemStack held =
      Player.ActiveSlot.Itemstack
      ?? throw new InvalidOperationException("the hand holds nothing to put");
    int count = held.StackSize;
    cursor.Itemstack = held;
    Player.ActiveSlot.Itemstack = null;
    Click(inventory, slotId);
    ItemStack? left = cursor.Itemstack;
    Player.ActiveSlot.Itemstack = left;
    cursor.Itemstack = null;
    return left != null && left.Collectible == held.Collectible
      ? count - left.StackSize
      : count;
  }

  /// <summary>Takes the stack in slot <paramref name="slotId"/>: sends the left click on that slot
  /// the dialog sends with an empty cursor, and moves what reached the cursor into
  /// <see cref="TestPlayer.ActiveSlot"/>.</summary>
  /// <param name="slotId">The slot's index in the entity's inventory, from 0.</param>
  /// <returns>The stack now in the hand; null when nothing was taken.</returns>
  /// <exception cref="InvalidOperationException">The entity is not an
  /// <see cref="IBlockEntityContainer"/> with an inventory, the hand holds a stack, or the cursor
  /// already holds one.</exception>
  public ItemStack? Take(int slotId) {
    InventoryBase inventory = Container();
    ItemSlot cursor = Cursor();
    if (!Player.ActiveSlot.Empty)
      throw new InvalidOperationException("the hand must be empty to take");
    Click(inventory, slotId);
    Player.ActiveSlot.Itemstack = cursor.Itemstack;
    cursor.Itemstack = null;
    return Player.ActiveSlot.Itemstack;
  }

  private void Click(InventoryBase inventory, int slotId) {
    var op = new ItemStackMoveOperation(
      Player.Player.Entity.World,
      EnumMouseButton.Left,
      0,
      EnumMergePriority.DirectMerge
    );
    var packet = (Packet_Client)
      inventory.InvNetworkUtil.GetActivateSlotPacket(slotId, op);
    Send(packet.Id, Packet_ClientSerializer.SerializeToBytes(packet));
  }

  private void Send(int packetId, byte[]? data) =>
    Entity.OnReceivedClientPacket(Player.Player, packetId, data);

  private InventoryBase Container() =>
    Entity is IBlockEntityContainer { Inventory: InventoryBase inventory }
      ? inventory
      : throw new InvalidOperationException(
        $"{Entity.GetType().Name} has no inventory to click"
      );

  private ItemSlot Cursor() {
    ItemSlot cursor = Player.Mouse[0];
    if (!cursor.Empty)
      throw new InvalidOperationException("the cursor already holds a stack");
    return cursor;
  }
}
