using System.Collections.Generic;
using ExpandedLib.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ExpandedLib.Machines;

/// <summary>
/// Base block entity for a machine worked through a window: a container whose slots are declared
/// as <see cref="MachineSlotSpec"/>s, plus the open/close handshake with the client.
/// </summary>
public abstract class BlockEntityMachineStation : BlockEntityContainer {
  // 1000/1001 are the vanilla open/close packet ids; ids below 1000 belong to the inventory's own
  // slot-move protocol.
  private const int PacketIdOpen = 1000;
  private const int PacketIdClose = 1001;

  /// <summary>First packet id free for a machine's own actions; ids below this are the container
  /// protocol.</summary>
  public const int FirstMachinePacketId = 1002;

  private MachineStationInventory? _inventory;
  private ExBlockState? _state;

  /// <summary>The slots this machine offers, in window order, read once when the inventory is first
  /// built.</summary>
  protected abstract MachineSlotSpec[] SlotSpecs { get; }

  /// <summary>This station's declared fields, built on first use.</summary>
  protected ExBlockState Persisted =>
    BlockEntityStateHost.GetOrCreate(this, ref _state, DeclareState);

  /// <summary>Declares the fields this station persists beyond its inventory; called once, lazily.</summary>
  protected virtual void DeclareState(ExBlockState state) { }

  public override InventoryBase Inventory =>
    _inventory ??= new MachineStationInventory(SlotSpecs);

  // The open window. Client-side only; null when closed.
  private GuiDialogBlockEntity? _dialog;

  /// <summary>Whether this machine's window is open on this client.</summary>
  protected bool WindowOpen => _dialog != null;

  /// <summary>Builds this machine's window, client-side, called on each open; the dialog must use
  /// this station's <see cref="BlockEntity.Pos"/> and call base <c>OnGuiClosed</c>.</summary>
  protected virtual GuiDialogBlockEntity? CreateDialog(ICoreClientAPI capi) =>
    null;

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    Inventory.LateInitialize($"{InventoryClassName}-{Pos}", api);
  }

  #region Declared state

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    Persisted.ToTree(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    Persisted.FromTree(tree, worldForResolving);
  }

  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  ) {
    base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
    Persisted.StoreCollectibleMappings(
      Api.World,
      blockIdMapping,
      itemIdMapping
    );
  }

  public override void OnLoadCollectibleMappings(
    IWorldAccessor worldForResolve,
    Dictionary<int, AssetLocation> oldBlockIdMapping,
    Dictionary<int, AssetLocation> oldItemIdMapping,
    int schematicSeed,
    bool resolveImports
  ) {
    base.OnLoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping,
      schematicSeed,
      resolveImports
    );
    Persisted.LoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping
    );
  }

  #endregion

  #region The window

  /// <summary>Right-click entry point: toggles the window; a no-op on the server.</summary>
  public void ToggleWindow(IPlayer byPlayer) {
    if (Api.Side == EnumAppSide.Client)
      ToggleDialog((ICoreClientAPI)Api, byPlayer);
  }

  private void ToggleDialog(ICoreClientAPI capi, IPlayer byPlayer) {
    if (_dialog != null) {
      _dialog.TryClose();
      return;
    }

    // A windowless station sends no open packet.
    _dialog = CreateDialog(capi);
    if (_dialog == null)
      return;

    // A refused TryOpen (duplicate dialog) gets disposed, not kept.
    if (!_dialog.TryOpen()) {
      _dialog.Dispose();
      _dialog = null;
      return;
    }

    // Base OnGuiClosed sends the close packet; an override must call base.
    _dialog.OnClosed += () => {
      _dialog?.Dispose();
      _dialog = null;
    };

    capi.Network.SendPacketClient(Inventory.Open(byPlayer));
    capi.Network.SendBlockEntityPacket(Pos, PacketIdOpen);
  }

  /// <summary>Closes and disposes the window.</summary>
  protected virtual void CloseWindow() {
    if (_dialog?.IsOpened() == true)
      _dialog.TryClose();
    _dialog?.Dispose();
    _dialog = null;
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    CloseWindow();
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    CloseWindow();
  }

  #endregion

  #region Packet handshake

  /// <summary>Server-side routing for the window packets: container protocol, access check, then a
  /// machine's own actions.</summary>
  public sealed override void OnReceivedClientPacket(
    IPlayer player,
    int packetid,
    byte[] data
  ) {
    // Close needs no access check; vanilla closes unconditionally too.
    if (packetid == PacketIdClose) {
      player.InventoryManager?.CloseInventory(Inventory);
      return;
    }

    if (!MayUse(player)) {
      // A refused slot move triggers a rollback to resync the client's view.
      if (packetid < PacketIdOpen && player is IServerPlayer serverPlayer)
        SendRollback(serverPlayer, packetid, data);
      return;
    }

    if (packetid < PacketIdOpen) {
      Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
      // Marks the chunk modified: a slot move needs an explicit write to persist across a restart.
      Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
      return;
    }

    if (packetid == PacketIdOpen) {
      player.InventoryManager?.OpenInventory(Inventory);
      return;
    }

    if (OnStationPacket(player, packetid, data))
      return;

    base.OnReceivedClientPacket(player, packetid, data);
  }

  /// <summary>This machine's own window actions, run server-side after the access check; returns
  /// true when handled.</summary>
  protected virtual bool OnStationPacket(
    IPlayer player,
    int packetid,
    byte[] data
  ) => false;

  #endregion

  #region Access

  // Enables the engine's interaction-range test; off only for headless tests, never gating the
  // claim check.
  internal bool ValidatePickRange { get; set; } = true;

  /// <summary>Whether <paramref name="player"/> may act on this machine: claim access, plus the
  /// engine's interaction-range test on 1.22 and later.</summary>
  private bool MayUse(IPlayer player) {
#if GAME_GE_1_22
    // CachedAccessPerms runs both the claim check and the range test, and audits either failure.
#pragma warning disable CS0618 // The ctor is obsolete ahead of a 1.23 signature change; there is no other entry point yet.
    var perms = new CachedAccessPerms(Api.World, Pos, player);
#pragma warning restore CS0618
    return perms.IsInteractingPlayerAllowedTo(
      EnumBlockAccessFlags.Use,
      ValidatePickRange,
      "machine station"
    );
#else
    // 1.20/1.21 have no public reach test; those builds keep the claim check alone.
    if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use))
      return true;

    Api.World.Logger.Audit(
      "Player {0} sent a machine-station packet to {1} without claim access. Rejected.",
      player.PlayerName,
      Pos
    );
    return false;
#endif
  }

  // Rolls the client's inventory view back to the server's; legacy builds correct only on reopen.
  private void SendRollback(IServerPlayer player, int packetid, byte[] data) {
#if GAME_GE_1_22
    Inventory.InvNetworkUtil.SendInventoryRollback(player, packetid, data);
#endif
  }

  #endregion
}
