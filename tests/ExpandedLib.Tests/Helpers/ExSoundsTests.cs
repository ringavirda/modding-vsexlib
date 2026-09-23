using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The machine-sound volume, the clip-length throttle, the server-to-client one-shot
/// channel and <see cref="ExSoundLoop"/>'s lifetime.</summary>
[Collection(ExSoundsCollection.Name)]
public class ExSoundsTests : IDisposable {
  private static readonly BlockPos At = new(10, 5, 20);

  public void Dispose() {
    ExSounds.MachineVolume = 1f;
    ExSounds.StopServer();
  }

  #region Catalogue and one-shots

  // Fails when a catalogue length is shortened below its file (Fire 9260 -> 9000) or an entry is
  // dropped from the length table.
  [Fact]
  public void Every_catalogue_sound_lasts_at_least_its_longest_file() {
    string assets = Path.Combine(
      VsAssemblyResolver.InstallPath
        ?? throw new InvalidOperationException("No game install."),
      "assets"
    );
    var offenders = new List<string>();
    int measured = 0;
    foreach (FieldInfo field in CatalogueFields()) {
      var location = (AssetLocation)field.GetValue(null)!;
      double longest = Variants(assets, location.Path)
        .Select(OggLengthMs)
        .DefaultIfEmpty(0)
        .Max();
      if (longest == 0) {
        offenders.Add($"{field.Name}: no file for {location}");
        continue;
      }
      measured++;
      long recorded = ExSounds.ClipLengthMs(location);
      if (recorded < longest || recorded > longest + 1)
        offenders.Add(
          $"{field.Name}: recorded {recorded} ms, file {longest:0.0} ms"
        );
    }
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    Assert.True(measured > 30, $"Only {measured} catalogue sounds measured.");
  }

  // Fails when Due ignores the clip length and honours the caller's interval alone.
  [Fact]
  public void A_repeating_one_shot_waits_for_its_clip_to_end() {
    IWorldAccessor world = ClientWorld(
      out Func<long> _,
      out Action<long> setNow
    );
    long last = 0;
    long clip = ExSounds.ClipLengthMs(ExSounds.Fire);

    setNow(100_000);
    ExSounds.PlayLoop(world, At, ExSounds.Fire, ref last, 1000);
    setNow(100_000 + clip - 1);
    ExSounds.PlayLoop(world, At, ExSounds.Fire, ref last, 1000);
    Assert.Single(Plays(world));

    setNow(100_000 + clip);
    ExSounds.PlayLoop(world, At, ExSounds.Fire, ref last, 1000);
    Assert.Equal(2, Plays(world).Count);
  }

  // Fails when Due takes the clip length over a longer caller interval.
  [Fact]
  public void A_longer_interval_than_the_clip_still_holds() {
    IWorldAccessor world = ClientWorld(
      out Func<long> _,
      out Action<long> setNow
    );
    long last = 0;

    setNow(100_000);
    ExSounds.PlayLoop(world, At, ExSounds.Latch, ref last, 5000);
    setNow(104_999);
    ExSounds.PlayLoop(world, At, ExSounds.Latch, ref last, 5000);
    Assert.Single(Plays(world));
  }

  // Fails when PlayOnClient drops the MachineVolume factor.
  [Fact]
  public void A_client_play_is_scaled_by_the_machine_volume() {
    IWorldAccessor world = ClientWorld(out Func<long> _, out Action<long> _);
    ExSounds.MachineVolume = 0.5f;

    ExSounds.PlayLocal(world, At, ExSounds.Latch, 0.8f);

    object?[] args = Assert.Single(Plays(world));
    Assert.Equal(0.4f, (float)args[^1]!, 3);
#if GAME_GE_1_22
    Assert.Equal(EnumSoundType.Sound, ((SoundAttributes)args[0]!).Type);
#endif
  }

  // Fails when PlayOnClient plays at zero volume instead of skipping.
  [Fact]
  public void A_muted_machine_volume_plays_nothing() {
    IWorldAccessor world = ClientWorld(out Func<long> _, out Action<long> _);
    ExSounds.MachineVolume = 0f;

    ExSounds.PlayLocal(world, At, ExSounds.Latch);

    Assert.Empty(Plays(world));
  }

  // Fails when the MachineVolume setter stops clamping.
  [Fact]
  public void The_machine_volume_is_clamped_to_zero_to_one() {
    ExSounds.MachineVolume = 3f;
    Assert.Equal(1f, ExSounds.MachineVolume);
    ExSounds.MachineVolume = -1f;
    Assert.Equal(0f, ExSounds.MachineVolume);
  }

  // Fails when the client handler skips MachineVolume, or Emit sends through the channel and
  // the game's broadcast both.
  [Fact]
  public void A_server_one_shot_reaches_a_listening_client_in_range() {
    IClientWorldAccessor client = Hear(
      Listener.Near,
      out TestWorld tw,
      out TestChannels pair
    );

    var packet = (SoundPacket)Assert.Single(pair.SentToClients);
    Assert.Equal(ExSounds.Latch.ToString(), packet.Sound);
    Assert.Equal(0.6f, packet.Volume, 3);
    object?[] args = Assert.Single(Plays(client));
    Assert.Equal(0.3f, (float)args[^1]!, 3);
    tw.World.DidNotReceiveWithAnyArgs()
      .PlaySoundAt(default(AssetLocation), 0, 0, 0, null, true, 0, 0);
  }

  // Each fails when Emit drops the one filter that rejects that listener: the player who
  // triggered the sound, a player still joining, a player out of range.
  [Theory]
  [InlineData(Listener.Trigger)]
  [InlineData(Listener.Joining)]
  [InlineData(Listener.Far)]
  public void A_server_one_shot_skips_a_listener_it_must_not_reach(
    Listener role
  ) {
    IClientWorldAccessor client = Hear(
      role,
      out TestWorld _,
      out TestChannels pair
    );

    Assert.Empty(pair.SentToClients);
    Assert.Empty(Plays(client));
  }

  // Fails when Emit drops the fallback to the game's own broadcast before the channel opens.
  [Fact]
  public void A_server_one_shot_without_the_channel_uses_the_games_broadcast() {
    var tw = new TestWorld();
    ExSounds.StopServer();

    ExSounds.Play(tw.Api, At, ExSounds.Latch, 0.7f);

    tw.World.Received(1)
      .PlaySoundAt(
        ExSounds.Latch,
        At.X + 0.5,
        At.Y + 0.5,
        At.Z + 0.5,
        null,
        true,
        24f,
        0.7f
      );
  }

  // Fails when Play drops its server gate and plays on a client too.
  [Fact]
  public void Play_is_silent_on_a_client() {
    ICoreClientAPI capi = Substitute.For<ICoreClientAPI>();
    capi.Side.Returns(EnumAppSide.Client);
    IClientWorldAccessor world = Substitute.For<IClientWorldAccessor>();
    world.Side.Returns(EnumAppSide.Client);
    capi.World.Returns(world);
    ((ICoreAPI)capi).World.Returns(world);

    ExSounds.Play(capi, At, ExSounds.Latch);

    Assert.Empty(Plays(world));
  }

  #endregion

  #region Loops

  // Fails when Update loads a new sound on each call instead of reusing the first.
  [Fact]
  public void A_loop_loads_once_and_restarts_the_same_sound() {
    ICoreClientAPI capi = LoopApi(out List<ILoadedSound> loaded);
    var loop = new ExSoundLoop(ExSounds.Fire, 0.5f);

    loop.Update(capi, At, true);
    loop.Update(capi, At, false);
    loop.Update(capi, At, true);

    ILoadedSound sound = Assert.Single(loaded);
    Assert.True(loop.IsPlaying);
    sound.Received(1).Stop();
    sound.Received(2).Start();
  }

  // Fails when Update skips the stop for a machine that is not running.
  [Fact]
  public void A_loop_stops_when_its_machine_stops() {
    ICoreClientAPI capi = LoopApi(out List<ILoadedSound> _);
    var loop = new ExSoundLoop(ExSounds.Fire);

    loop.Update(capi, At, true);
    loop.Update(capi, At, false);

    Assert.False(loop.IsPlaying);
  }

  // Fails when Update stops re-applying the volume after MachineVolume changes, or loads the
  // sound without the MachineVolume factor.
  [Fact]
  public void A_loop_follows_the_machine_volume() {
    ICoreClientAPI capi = LoopApi(
      out List<ILoadedSound> loaded,
      out List<SoundParams> made
    );
    ExSounds.MachineVolume = 0.5f;
    var loop = new ExSoundLoop(ExSounds.Fire, 0.8f, pitch: 0.9f);

    loop.Update(capi, At, true);
    ExSounds.MachineVolume = 0.25f;
    loop.Update(capi, At, true);
    loop.Pitch = 1.1f;
    loop.Update(capi, At, true);

    SoundParams first = Assert.Single(made);
    Assert.Equal(0.4f, first.Volume, 3);
    Assert.Equal(EnumSoundType.Ambient, first.SoundType);
    Assert.True(first.ShouldLoop);
    loaded[0].Received(1).SetVolume(0.2f);
    loaded[0].Received(1).SetPitch(1.1f);
  }

  // Fails when Dispose leaves the sound loaded, or a disposed loop loads again on Update.
  [Fact]
  public void A_disposed_loop_is_released_and_never_loads_again() {
    ICoreClientAPI capi = LoopApi(out List<ILoadedSound> loaded);
    var loop = new ExSoundLoop(ExSounds.Fire);

    loop.Update(capi, At, true);
    loop.Dispose();
    loop.Update(capi, At, true);

    ILoadedSound sound = Assert.Single(loaded);
    sound.Received(1).Dispose();
    Assert.True(loop.IsDisposed);
    Assert.False(loop.IsPlaying);
  }

  #endregion

  #region Command

  // Fails when the command stops applying the chosen volume, or accepts a value outside the
  // preference's tenths.
  [Fact]
  public void The_sound_command_sets_and_reports_the_volume() {
    var pref = new SoundVolumePreference();
    ExPreferences.Register(pref);

    TextCommandResult set = SoundSubCommand.Dispatch("uid-sound", pref, "0.3");
    TextCommandResult shown = SoundSubCommand.Dispatch("uid-sound", pref, null);
    TextCommandResult bad = SoundSubCommand.Dispatch("uid-sound", pref, "0.35");

    Assert.Equal(EnumCommandStatus.Success, set.Status);
    Assert.Equal(0.3f, ExSounds.MachineVolume, 3);
    Assert.Equal("0.3", ExPreferences.GetForPlayer("uid-sound", "sound"));
    Assert.Equal(EnumCommandStatus.Success, shown.Status);
    Assert.Equal("exlib:command-pref-current", shown.StatusMessage);
    Assert.Equal(EnumCommandStatus.Error, bad.Status);
    Assert.Equal(0.3f, ExSounds.MachineVolume, 3);
  }

  #endregion

  private static IEnumerable<FieldInfo> CatalogueFields() =>
    typeof(ExSounds)
      .GetFields(BindingFlags.Public | BindingFlags.Static)
      .Where(f => f.FieldType == typeof(AssetLocation));

  // A catalogue path names one file, or a numbered set the game picks from at random.
  private static IEnumerable<string> Variants(string assets, string path) {
    var stem = new Regex(
      "^" + Regex.Escape(Path.GetFileName(path)) + @"\d*\.ogg$"
    );
    foreach (string domain in new[] { "game", "survival", "creative" }) {
      string dir = Path.Combine(assets, domain, Path.GetDirectoryName(path)!);
      if (!Directory.Exists(dir))
        continue;
      foreach (string file in Directory.EnumerateFiles(dir, "*.ogg"))
        if (stem.IsMatch(Path.GetFileName(file)))
          yield return file;
    }
  }

  // Length of a Vorbis stream in milliseconds: the last page's granule position over the sample
  // rate in the identification header.
  private static double OggLengthMs(string file) {
    byte[] data = File.ReadAllBytes(file);
    int id = IndexOf(data, "\u0001vorbis"u8.ToArray(), 0);
    uint rate = BitConverter.ToUInt32(data, id + 12);
    int last = -1;
    for (int at = 0; (at = IndexOf(data, "OggS"u8.ToArray(), at)) >= 0; at++)
      last = at;
    long granule = BitConverter.ToInt64(data, last + 6);
    return granule * 1000.0 / rate;
  }

  private static int IndexOf(byte[] data, byte[] needle, int from) =>
    data.AsSpan(from).IndexOf(needle) is var i and >= 0 ? from + i : -1;

  private static IWorldAccessor ClientWorld(
    out Func<long> now,
    out Action<long> setNow
  ) {
    long ms = 0;
    IWorldAccessor world = Substitute.For<IWorldAccessor>();
    world.Side.Returns(EnumAppSide.Client);
    world.ElapsedMilliseconds.Returns(_ => ms);
    now = () => ms;
    setNow = v => ms = v;
    return world;
  }

  private static List<object?[]> Plays(IWorldAccessor world) =>
    world
      .ReceivedCalls()
      .Where(c => c.GetMethodInfo().Name == nameof(IWorldAccessor.PlaySoundAt))
      .Select(c => c.GetArguments())
      .ToList();

  /// <summary>Who the channel's one client is, relative to a server one-shot.</summary>
  public enum Listener {
    Near,
    Trigger,
    Joining,
    Far,
  }

  // Plays a server one-shot at At with the channel's sender cast as role; returns the client
  // world the sender's plays land in.
  private static IClientWorldAccessor Hear(
    Listener role,
    out TestWorld tw,
    out TestChannels pair
  ) {
    tw = new TestWorld();
    pair = tw.Channels(ExSounds.ChannelName);
    IServerPlayer sender = pair.Sender;
    sender.ConnectionState.Returns(
      role == Listener.Joining
        ? EnumClientState.Connected
        : EnumClientState.Playing
    );
    sender.Entity.Pos.SetPos(
      At.X + (role == Listener.Far ? 40.5 : 0.5),
      At.Y + 0.5,
      At.Z + 0.5
    );
    tw.World.AllOnlinePlayers.Returns([sender]);
    IClientWorldAccessor client = Substitute.For<IClientWorldAccessor>();
    client.Side.Returns(EnumAppSide.Client);
    tw.ClientApi.World.Returns(client);
    ExSounds.StartServer(tw.Api);
    ExSounds.StartClient(tw.ClientApi);
    ExSounds.MachineVolume = 0.5f;

    ExSounds.PlayAt(
      tw.World,
      At,
      ExSounds.Latch,
      role == Listener.Trigger ? sender : null,
      range: 24f,
      volume: 0.6f
    );
    return client;
  }

  private static ICoreClientAPI LoopApi(out List<ILoadedSound> loaded) =>
    LoopApi(out loaded, out List<SoundParams> _);

  private static ICoreClientAPI LoopApi(
    out List<ILoadedSound> loaded,
    out List<SoundParams> made
  ) {
    var sounds = new List<ILoadedSound>();
    var calls = new List<SoundParams>();
    ICoreClientAPI capi = Substitute.For<ICoreClientAPI>();
    capi.Side.Returns(EnumAppSide.Client);
    IClientWorldAccessor world = Substitute.For<IClientWorldAccessor>();
    capi.World.Returns(world);
    world
      .LoadSound(Arg.Any<SoundParams>())
      .Returns(ci => {
        calls.Add(ci.Arg<SoundParams>());
        ILoadedSound sound = Substitute.For<ILoadedSound>();
        bool playing = false;
        sound.IsPlaying.Returns(_ => playing);
        sound.When(s => s.Start()).Do(_ => playing = true);
        sound.When(s => s.Stop()).Do(_ => playing = false);
        sounds.Add(sound);
        return sound;
      });
    loaded = sounds;
    made = calls;
    return capi;
  }
}

/// <summary>Serializes every test class touching <see cref="ExSounds.MachineVolume"/> or the
/// sound channel, both process-global.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ExSoundsCollection {
  public const string Name = "ExSounds";
}
