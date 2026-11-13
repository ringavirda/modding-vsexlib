using System.ComponentModel;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Assembling;

/// <summary>Carries hand assembly's fixes and take-offs: the client sends an <c>AssemblyFix</c> or an <c>AssemblyTakeOff</c> on the <c>exlib:assembly</c> channel and the server places or takes off the part, or answers an <c>AssemblyResult</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class HandAssemblyModSystem : ModSystem {
  internal const string ChannelName = "exlib:assembly";

  private ICoreServerAPI? _sapi;
  private IServerNetworkChannel? _serverChannel;

  /// <inheritdoc/>
  public override void StartClientSide(ICoreClientAPI api) {
    IClientNetworkChannel channel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<AssemblyFix>()
      .RegisterMessageType<AssemblyTakeOff>()
      .RegisterMessageType<AssemblyResult>();
    var client = new HandAssemblyClient(api, channel);
    channel.SetMessageHandler<AssemblyResult>(client.ShowResult);
  }

  /// <inheritdoc/>
  public override void StartServerSide(ICoreServerAPI api) {
    _sapi = api;
    _serverChannel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<AssemblyFix>()
      .RegisterMessageType<AssemblyTakeOff>()
      .RegisterMessageType<AssemblyResult>()
      .SetMessageHandler<AssemblyFix>(OnFix)
      .SetMessageHandler<AssemblyTakeOff>(OnTakeOff);
  }

  private void OnTakeOff(IServerPlayer player, AssemblyTakeOff take) {
    AssemblyResult? refusal = HandAssemblyServer.TakeOff(
      _sapi!.World,
      player,
      take
    );
    if (refusal != null)
      _serverChannel!.SendPacket(refusal, player);
  }

  private void OnFix(IServerPlayer player, AssemblyFix fix) {
    AssemblyResult? refusal = HandAssemblyServer.Fix(_sapi!.World, player, fix);
    if (refusal != null)
      _serverChannel!.SendPacket(refusal, player);
  }
}
