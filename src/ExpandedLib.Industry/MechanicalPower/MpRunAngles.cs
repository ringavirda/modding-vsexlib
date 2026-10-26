using System;
using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// The angle of each "mpshaft" run on a client, kept per run anchor and advanced between server packets at the
/// last speed sent, and the channel the server sends them on.
/// </summary>
internal sealed class MpRunAngles {
  internal const string ChannelName = "exlibMpTurn";
  private const string CacheKey = "exlib:mprunangles";

  /// <summary>Gap in rad between the shown and the server angle above which the shown angle snaps to the server's.</summary>
  internal const double SnapRad = Math.PI / 2;

  /// <summary>Seconds in which a gap under <see cref="SnapRad"/> closes.</summary>
  internal const double CatchUpSeconds = 0.5;

  /// <summary>Milliseconds without a packet after which a run counts as stopped.</summary>
  internal const long SilenceMs = 10_000;

  private static Action<MpRunAnglePacket>? _serverSend;

  private readonly Dictionary<NodeKey, Run> _runs = [];

  /// <summary>One run's angle as a client shows it.</summary>
  internal sealed class Run {
    /// <summary>Shown reference angle in rad, unwrapped.</summary>
    public double Angle;

    /// <summary>The server's reference angle carried forward at <see cref="Speed"/>.</summary>
    public double ServerAngle;

    /// <summary>Reference speed in rad/s, signed.</summary>
    public float Speed;

    /// <summary>Client clock in ms at the last packet that named the run.</summary>
    public long LastPacketMs;
  }

  /// <summary>Opens the server end of the channel; <see cref="Send"/> broadcasts through it from then on.</summary>
  internal static void StartServer(ICoreServerAPI api) {
    IServerNetworkChannel channel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<MpRunAnglePacket>();
    _serverSend = packet => channel.BroadcastPacket(packet);
  }

  /// <summary>Opens the client end of the channel, feeding each packet to the client's <see cref="For"/> state.</summary>
  internal static void StartClient(ICoreClientAPI api) =>
    api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<MpRunAnglePacket>()
      .SetMessageHandler<MpRunAnglePacket>(packet =>
        For(api).Receive(packet, api.ElapsedMilliseconds)
      );

  /// <summary>Closes the server end; later packets go nowhere.</summary>
  internal static void StopServer() => _serverSend = null;

  /// <summary>Redirects <see cref="Send"/>, so a test reads the packets without a network.</summary>
  internal static void SetServerSend(Action<MpRunAnglePacket>? send) =>
    _serverSend = send;

  /// <summary>Broadcasts a packet to every player; does nothing while the channel is closed.</summary>
  internal static void Send(MpRunAnglePacket packet) =>
    _serverSend?.Invoke(packet);

  /// <summary>The state of one client, made on first use and kept in the client's object cache.</summary>
  internal static MpRunAngles For(ICoreClientAPI api) =>
    ObjectCacheUtil.GetOrCreate(api, CacheKey, () => new MpRunAngles());

  /// <summary>Takes a packet: a run not seen before starts at its angle, a known one is told its server angle and speed.</summary>
  /// <param name="packet">Parallel arrays, one entry per run.</param>
  /// <param name="nowMs">The client clock in ms.</param>
  internal void Receive(MpRunAnglePacket packet, long nowMs) {
    for (int i = 0; i < packet.Angle.Length; i++) {
      var anchor = new NodeKey(
        new BlockPos(packet.X[i], packet.Y[i], packet.Z[i]),
        (byte)packet.Slot[i]
      );
      if (!_runs.TryGetValue(anchor, out Run? run))
        _runs[anchor] = run = new Run { Angle = packet.Angle[i] };
      run.ServerAngle = packet.Angle[i];
      run.Speed = packet.Speed[i];
      run.LastPacketMs = nowMs;
    }
  }

  /// <summary>Advances every run by one frame: both angles at the run's speed, then the shown one toward the server's.</summary>
  /// <param name="dt">Frame time in seconds.</param>
  /// <param name="nowMs">The client clock in ms; a run silent for <see cref="SilenceMs"/> stops.</param>
  internal void Advance(float dt, long nowMs) {
    foreach (Run run in _runs.Values) {
      if (nowMs - run.LastPacketMs > SilenceMs)
        run.Speed = 0f;
      run.Angle += run.Speed * dt;
      run.ServerAngle += run.Speed * dt;
      double gap = run.ServerAngle - run.Angle;
      if (Math.Abs(gap) > SnapRad)
        run.Angle = run.ServerAngle;
      else
        run.Angle += gap * Math.Min(1d, dt / CatchUpSeconds);
    }
  }

  /// <summary>The run anchored at <paramref name="anchor"/>, or false when no packet has named it.</summary>
  internal bool TryGet(NodeKey anchor, out Run run) =>
    _runs.TryGetValue(anchor, out run!);
}
