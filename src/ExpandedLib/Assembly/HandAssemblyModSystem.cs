using System.ComponentModel;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Assembling;

/// <summary>Carries hand assembly's fixes: the client sends an <c>AssemblyFix</c> on the <c>exlib:assembly</c> channel and the server places the part or answers an <c>AssemblyResult</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class HandAssemblyModSystem : ModSystem {
  internal const string ChannelName = "exlib:assembly";

  private ICoreServerAPI? _sapi;
  private IServerNetworkChannel? _serverChannel;

  /// <inheritdoc/>
  public override void StartClientSide(ICoreClientAPI api) =>
    api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<AssemblyFix>()
      .RegisterMessageType<AssemblyResult>();

  /// <inheritdoc/>
  public override void StartServerSide(ICoreServerAPI api) {
    _sapi = api;
    _serverChannel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<AssemblyFix>()
      .RegisterMessageType<AssemblyResult>()
      .SetMessageHandler<AssemblyFix>(OnFix);
  }

  private void OnFix(IServerPlayer player, AssemblyFix fix) {
    AssemblyResult? refusal = HandAssemblyServer.Fix(_sapi!.World, player, fix);
    if (refusal != null)
      _serverChannel!.SendPacket(refusal, player);
  }
}
