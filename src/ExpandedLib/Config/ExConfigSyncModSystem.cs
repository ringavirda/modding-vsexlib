using System.ComponentModel;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Config;

/// <summary>Syncs every registered config section's live values from the host to each client.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExConfigSyncModSystem : ModSystem {
  private const string ChannelName = "exlibConfigSync";

  #region Server
  private IServerNetworkChannel? _serverChannel;

  public override void StartServerSide(ICoreServerAPI api) {
    _serverChannel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<ConfigSyncPacket>();
    api.Event.PlayerJoin += SendAllSections;
  }

  /// <summary>Sends every registered section to one player, once their client is ready to receive it.</summary>
  private void SendAllSections(IServerPlayer player) {
    foreach (var code in ExConfigProfiles.Codes)
      if (ExConfigProfiles.TryGet(code, out var config))
        _serverChannel!.SendPacket(ToPacket(config), player);
  }

  /// <summary>Pushes one section's current values to every connected player.</summary>
  public void BroadcastSection(IExConfigAccess config) =>
    _serverChannel?.BroadcastPacket(ToPacket(config));

  private static ConfigSyncPacket ToPacket(IExConfigAccess config) =>
    new() {
      ModId = config.ModId,
      FileName = config.FileName,
      Json = config.ExportJson(),
    };
  #endregion

  #region Client
  public override void StartClientSide(ICoreClientAPI api) {
    api.Network.RegisterChannel(ChannelName)
      .RegisterMessageType<ConfigSyncPacket>()
      .SetMessageHandler<ConfigSyncPacket>(packet => HandlePacket(api, packet));
  }

  /// <summary>Imports one section received from the host into its matching registered store, or logs
  /// a warning and drops it if this side has no such section registered.</summary>
  internal static void HandlePacket(ICoreClientAPI api, ConfigSyncPacket packet) {
    if (!ExConfigProfiles.TryGet(packet.ModId, out var config)) {
      api.Logger.Warning(
        "[exlib] config: unknown section '{0}' received from the server; ignored.",
        packet.ModId
      );
      return;
    }

    config.ImportJson(packet.Json);
    api.Logger.Notification(
      "[exlib] config: {0} section received from the server",
      packet.ModId
    );
  }
  #endregion
}
