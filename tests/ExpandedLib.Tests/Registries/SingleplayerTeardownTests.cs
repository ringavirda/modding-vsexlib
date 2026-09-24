using System;
using System.Reflection;
using ExpandedLib.Industry;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using HarmonyLib;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>In singleplayer the client's teardown leaves what the server applied: exlib's Harmony
/// patches, the no-snow marks and the server end of the sound channel. Patches <see cref="Block"/>
/// process-wide, hence the Harmony collection.</summary>
[Collection(ExHarmonyCollection.Name)]
public class SingleplayerTeardownTests : IDisposable {
  private static readonly BlockPos At = new(10, 5, 20);

  public void Dispose() => ExSounds.StopServer();

  private static ExpandedLibModSystem Exlib(TestWorld world) {
    var system = new ExpandedLibModSystem();
    ReflectionHelpers.SetProperty(
      system,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );
    return system;
  }

  // The first assert fails when UnpatchAll unpatches with a hold left, the second when Dispose
  // empties NoSnowCells again, the last when the holds never reach zero.
  [Fact]
  public void A_singleplayer_client_dispose_leaves_the_servers_patches_and_marks() {
    var world = new TestWorld();
    world.ClientApi.IsSinglePlayer.Returns(true);
    ExpandedLibModSystem server = Exlib(world);
    ExpandedLibModSystem client = Exlib(world);
    var pos = new BlockPos(1, 2, 3, 0);
    object owner = new();
    MethodBase target = AccessTools.Method(
      typeof(Block),
      nameof(Block.AllowSnowCoverage)
    );
    try {
      server.Start(world.Api);
      client.Start(world.ClientApi);
      NoSnowCells.Mark(owner, [pos]);

      client.Dispose();
      Assert.Contains(
        Harmony.GetPatchInfo(target)!.Postfixes,
        p => p.owner == "exlib"
      );
      Assert.True(NoSnowCells.IsMarked(pos));

      server.Dispose();
      Assert.False(
        Harmony.GetPatchInfo(target)?.Owners.Contains("exlib") ?? false
      );
    } finally {
      NoSnowCells.Unmark(owner);
      client.Dispose();
      server.Dispose();
    }
  }

  // Fails when IndustryModule.Dispose closes the channel on an instance that never opened it.
  [Fact]
  public void A_singleplayer_client_dispose_leaves_the_servers_sound_channel_open() {
    TestChannels pair = Listening(out TestWorld tw);
    new IndustryModule().StartServerSide(tw.Api);

    new IndustryModule().Dispose();
    ExSounds.PlayAt(tw.World, At, ExSounds.Latch, range: 24f, volume: 0.6f);

    Assert.Single(pair.SentToClients);
  }

  // Fails when StartServerSide stops recording that its instance opened the channel.
  [Fact]
  public void The_server_module_dispose_closes_the_channel_it_opened() {
    TestChannels pair = Listening(out TestWorld tw);
    var server = new IndustryModule();
    server.StartServerSide(tw.Api);

    server.Dispose();
    ExSounds.PlayAt(tw.World, At, ExSounds.Latch, range: 24f, volume: 0.6f);

    Assert.Empty(pair.SentToClients);
  }

  // One player online, playing and in range of At.
  private static TestChannels Listening(out TestWorld tw) {
    tw = new TestWorld();
    TestChannels pair = tw.Channels(ExSounds.ChannelName);
    IServerPlayer listener = pair.Sender;
    listener.ConnectionState.Returns(EnumClientState.Playing);
    listener.Entity.Pos.SetPos(At.X + 0.5, At.Y + 0.5, At.Z + 0.5);
    tw.World.AllOnlinePlayers.Returns([listener]);
    return pair;
  }
}
