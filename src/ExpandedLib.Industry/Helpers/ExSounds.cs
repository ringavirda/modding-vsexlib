using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Industry.Helpers;

/// <summary>Shared catalogue of sound asset locations and play helpers. Each helper states
/// whether it gates on side. A one-shot played on the server reaches each client in range through
/// the <c>exlibSound</c> channel once <see cref="IndustryModule"/> has opened it; every client-side
/// play and every loop is scaled by <see cref="MachineVolume"/> and plays as
/// <see cref="EnumSoundType.Sound"/>, the type vanilla machines use.</summary>
public static class ExSounds {
  // Molten / heat
  public static readonly AssetLocation Sizzle = new("game:sounds/sizzle");
  public static readonly AssetLocation MoltenMetal = new(
    "game:sounds/effect/moltenmetal"
  );
  public static readonly AssetLocation PourMetal = new("game:sounds/pourmetal");
  public static readonly AssetLocation Embers = new(
    "game:sounds/effect/embers"
  );
  public static readonly AssetLocation Fire = new(
    "game:sounds/environment/fire"
  );
  public static readonly AssetLocation Extinguish = new(
    "game:sounds/effect/extinguish1"
  );
  public static readonly AssetLocation Ignite = new("game:sounds/torch-ignite");

  // Mechanical / interaction
  public static readonly AssetLocation Latch = new("game:sounds/effect/latch");
  public static readonly AssetLocation CokeOvenDoorOpen = new(
    "game:sounds/block/cokeovendoor-open"
  );
  public static readonly AssetLocation CokeOvenDoorClose = new(
    "game:sounds/block/cokeovendoor-close"
  );
  public static readonly AssetLocation Bellows = new(
    "game:sounds/effect/bellows"
  );
  public static readonly AssetLocation Ingot = new("game:sounds/block/ingot");
  public static readonly AssetLocation AnvilHit = new(
    "game:sounds/effect/anvilhit1"
  );
  public static readonly AssetLocation AnvilHitShort = new(
    "game:sounds/effect/anvilhit"
  );
  public static readonly AssetLocation Build = new("game:sounds/player/build");
  public static readonly AssetLocation StoneCrush = new(
    "game:sounds/effect/stonecrush"
  );
  public static readonly AssetLocation ToggleSwitch = new(
    "game:sounds/toggleswitch"
  );

  /// <summary>Fired clay shattering - a crucible pot cracking or wearing out.</summary>
  public static readonly AssetLocation CeramicBreak = new(
    "game:sounds/block/ceramicbreak"
  );

  /// <summary>Heavy metal knock - an engine put back in order.</summary>
  public static readonly AssetLocation HeavyMetalHit = new(
    "game:sounds/block/heavymetal-hit"
  );

  // Fluids / venting
  public static readonly AssetLocation SmallSplash = new(
    "game:sounds/environment/smallsplash"
  );

  /// <summary>Vanilla barrel/container pour - used when manually filling the boiler.</summary>
  public static readonly AssetLocation WaterPour = new(
    "game:sounds/effect/water-pour"
  );

  /// <summary>Watering-can trickle - the rhythmic water sound of a working hand pump.</summary>
  public static readonly AssetLocation Watering = new(
    "game:sounds/effect/watering"
  );
  public static readonly AssetLocation ExtinguishHiss = new(
    "game:sounds/effect/extinguish"
  );

  // Steam-machine ambience / effects
  /// <summary>Cooking-pot bubble loop.</summary>
  public static readonly AssetLocation Cooking = new(
    "game:sounds/effect/cooking"
  );

  /// <summary>Lava bubble/rumble - the ambience of a pressurised gas pipe and a boiling boiler.</summary>
  public static readonly AssetLocation Lava = new(
    "game:sounds/environment/lava"
  );

  /// <summary>Gentle creek babble - repurposed as the ambience of a water-carrying pipe.</summary>
  public static readonly AssetLocation Creek = new(
    "game:sounds/environment/creek"
  );

  /// <summary>Iron-on-iron grind - the engine/sub-machine piston rising (up stroke).</summary>
  public static readonly AssetLocation MetalGrinding = new(
    "game:sounds/effect/metalgrinding"
  );

  /// <summary>Airy swoosh - gas venting from a freshly opened pipe end.</summary>
  public static readonly AssetLocation Swoosh = new(
    "game:sounds/effect/swoosh"
  );

  /// <summary>Torch un-equip whoosh - the engine/sub-machine piston rising (up stroke).</summary>
  public static readonly AssetLocation TorchUnequip = new(
    "game:sounds/held/torch-unequip"
  );

  /// <summary>Anvil merge clang - the engine/sub-machine piston bottoming out (down stroke).</summary>
  public static readonly AssetLocation AnvilMergeHit = new(
    "game:sounds/effect/anvilmergehit"
  );

  /// <summary>Planetary-gear churn - the constant low hum of a running engine's gear housing.</summary>
  public static readonly AssetLocation PlanetaryGears = new(
    "game:sounds/effect/planetary_gears"
  );

  /// <summary>Soft gearbox turning - the gear train of an engine's mechanical-power take-off.</summary>
  public static readonly AssetLocation GearboxTurn = new(
    "game:sounds/effect/gearbox_turn"
  );

  /// <summary>Large explosion - boiler burst. CreateExplosion plays its own; this is a spare.</summary>
  public static readonly AssetLocation LargeExplosion = new(
    "game:sounds/effect/largeexplosion"
  );

  /// <summary>Medium explosion - the muffled blast of an engine bursting.</summary>
  public static readonly AssetLocation MediumExplosion = new(
    "game:sounds/effect/mediumexplosion"
  );

  /// <summary>Small explosion - the muffled pop of a pipe bursting.</summary>
  public static readonly AssetLocation SmallExplosion = new(
    "game:sounds/effect/smallexplosion"
  );

  /// <summary>Client-side multiplier (0-1) on every sound these helpers and <see cref="ExSoundLoop"/>
  /// play, set from the player's <c>.exmod sound</c> preference; 1 by default. Values outside 0-1
  /// are clamped.</summary>
  public static float MachineVolume {
    get => _machineVolume;
    set => _machineVolume = Math.Clamp(value, 0f, 1f);
  }

  private static float _machineVolume = 1f;

  internal const string ChannelName = "exlibSound";

  private static IServerNetworkChannel? _serverChannel;

  // Milliseconds each catalogue sound lasts; a variant set (anvilhit1..3) holds its longest file.
  private static readonly Dictionary<AssetLocation, long> ClipLengths = new() {
    [Sizzle] = 3564,
    [MoltenMetal] = 3381,
    [PourMetal] = 3941,
    [Embers] = 22094,
    [Fire] = 9260,
    [Extinguish] = 859,
    [Ignite] = 3016,
    [Latch] = 229,
    [CokeOvenDoorOpen] = 648,
    [CokeOvenDoorClose] = 648,
    [Bellows] = 1415,
    [Ingot] = 210,
    [AnvilHit] = 259,
    [AnvilHitShort] = 1003,
    [Build] = 152,
    [StoneCrush] = 369,
    [ToggleSwitch] = 362,
    [CeramicBreak] = 791,
    [HeavyMetalHit] = 2469,
    [SmallSplash] = 1180,
    [WaterPour] = 1174,
    [Watering] = 1606,
    [ExtinguishHiss] = 890,
    [Cooking] = 4195,
    [Lava] = 56630,
    [Creek] = 29814,
    [MetalGrinding] = 3395,
    [Swoosh] = 899,
    [TorchUnequip] = 866,
    [AnvilMergeHit] = 1013,
    [PlanetaryGears] = 7749,
    [GearboxTurn] = 5500,
    [LargeExplosion] = 4047,
    [MediumExplosion] = 3786,
    [SmallExplosion] = 1503,
  };

  /// <summary>How long <paramref name="sound"/> plays, in milliseconds, or 0 for a sound outside the
  /// catalogue.</summary>
  public static long ClipLengthMs(AssetLocation sound) =>
    ClipLengths.TryGetValue(sound, out long ms) ? ms : 0;

  /// <summary>Plays a one-shot sound centred on <paramref name="pos"/> to the players in range. Server
  /// only.</summary>
  public static void Play(
    ICoreAPI? api,
    BlockPos pos,
    AssetLocation sound,
    float volume = 1f,
    float range = 24f
  ) {
    if (api == null || api.Side != EnumAppSide.Server)
      return;
    Emit(api.World, pos, sound, null, true, range, volume);
  }

  /// <summary>Plays at most once per <paramref name="intervalMs"/>, and never again before the clip
  /// has finished (<see cref="ClipLengthMs"/>). Server only.</summary>
  public static void PlayThrottled(
    ICoreAPI? api,
    BlockPos pos,
    AssetLocation sound,
    ref long lastMs,
    long intervalMs,
    float volume = 1f,
    float range = 24f
  ) {
    if (api == null || api.Side != EnumAppSide.Server)
      return;
    if (!Due(api.World, sound, ref lastMs, intervalMs))
      return;
    Emit(api.World, pos, sound, null, true, range, volume);
  }

  /// <summary>
  /// Plays a one-shot at <paramref name="pos"/> with no side gate - for client-side, animation-synced
  /// sounds that must play locally on each client (piston-stroke keyframe sounds). On the server it
  /// reaches the players in range.
  /// </summary>
  public static void PlayLocal(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    float volume = 1f,
    float range = 16f,
    bool randomizePitch = true
  ) => Emit(world, pos, sound, null, randomizePitch, range, volume);

  /// <summary>
  /// Like <see cref="PlayThrottled"/> but with no side gate - a client-safe repeat for ongoing
  /// ambience; it never plays again before the clip has finished (<see cref="ClipLengthMs"/>).
  /// </summary>
  public static void PlayLoop(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    ref long lastMs,
    long intervalMs,
    float volume = 1f,
    float range = 16f
  ) {
    if (!Due(world, sound, ref lastMs, intervalMs))
      return;
    Emit(world, pos, sound, null, false, range, volume);
  }

  /// <summary>
  /// Plays a one-shot at <paramref name="pos"/> through a world accessor, optionally excluding
  /// <paramref name="byPlayer"/> who triggered it.
  /// </summary>
  public static void PlayAt(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    IPlayer? byPlayer = null,
    bool randomizePitch = true,
    float range = 32f,
    float volume = 1f
  ) => Emit(world, pos, sound, byPlayer, randomizePitch, range, volume);

  /// <summary>
  /// Plays a sound only <paramref name="chance"/> (0-1) of the time, so a recurring event (a spill, a
  /// leak hiss) is audible without a constant roar.
  /// </summary>
  public static void PlayChance(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    double chance,
    bool randomizePitch = true,
    float range = 32f,
    float volume = 1f
  ) {
    if (world.Rand.NextDouble() >= chance)
      return;
    Emit(world, pos, sound, null, randomizePitch, range, volume);
  }

  /// <summary>Creates a gapless looping sound at <paramref name="volume"/> times
  /// <see cref="MachineVolume"/>. Returns null on the server; the caller owns the handle, and
  /// <see cref="ExSoundLoop"/> owns it for a machine.</summary>
  public static ILoadedSound? CreateLoop(
    ICoreAPI? api,
    BlockPos pos,
    AssetLocation sound,
    float volume = 1f,
    float range = 16f,
    float pitch = 1f
  ) {
    if (api is not ICoreClientAPI capi)
      return null;
    return capi.World.LoadSound(
      new SoundParams {
        Location = sound,
        ShouldLoop = true,
        Position = new Vec3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f),
        DisposeOnFinish = false,
        Volume = volume * MachineVolume,
        Range = range,
        Pitch = pitch,
        RelativePosition = false,
        SoundType = EnumSoundType.Sound,
      }
    );
  }

  /// <summary>Plays a quiet splash ~30% of the time, so a spill is audible without a roar.</summary>
  public static void SplashSound(IWorldAccessor world, BlockPos pos) =>
    PlayChance(world, pos, SmallSplash, 0.3);

  /// <summary>Plays a soft steam/gas hiss ~30% of the time, so venting gas is audible without a
  /// constant roar.</summary>
  public static void HissSound(IWorldAccessor world, BlockPos pos) =>
    PlayChance(world, pos, ExtinguishHiss, 0.3, range: 24f, volume: 0.5f);

  /// <summary>Opens the server end of the sound channel; one-shots played on the server go through it
  /// from then on.</summary>
  internal static void StartServer(ICoreServerAPI api) =>
    _serverChannel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<SoundPacket>();

  /// <summary>Opens the client end of the sound channel, playing each packet through
  /// <see cref="MachineVolume"/>.</summary>
  internal static void StartClient(ICoreClientAPI api) =>
    api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<SoundPacket>()
      .SetMessageHandler<SoundPacket>(packet =>
        PlayOnClient(
          api.World,
          packet.X,
          packet.Y,
          packet.Z,
          new AssetLocation(packet.Sound),
          packet.RandomizePitch,
          packet.Range,
          packet.Volume
        )
      );

  /// <summary>Closes the server end; later server one-shots fall back to the game's own
  /// broadcast.</summary>
  internal static void StopServer() => _serverChannel = null;

  // Advances lastMs and returns true once both the interval and the clip have run out.
  private static bool Due(
    IWorldAccessor world,
    AssetLocation sound,
    ref long lastMs,
    long intervalMs
  ) {
    long now = world.ElapsedMilliseconds;
    if (now - lastMs < Math.Max(intervalMs, ClipLengthMs(sound)))
      return false;
    lastMs = now;
    return true;
  }

  // A client plays the sound itself; a server sends it to the players in range, whose clients apply
  // their own MachineVolume.
  private static void Emit(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    IPlayer? byPlayer,
    bool randomizePitch,
    float range,
    float volume
  ) {
    double x = pos.X + 0.5,
      y = pos.Y + 0.5,
      z = pos.Z + 0.5;
    if (world.Side == EnumAppSide.Client) {
      PlayOnClient(world, x, y, z, sound, randomizePitch, range, volume);
      return;
    }
    if (_serverChannel == null) {
      world.PlaySoundAt(
        sound,
        x,
        y,
        z,
        byPlayer,
        randomizePitch,
        range,
        volume
      );
      return;
    }
    IServerPlayer[] listeners = world
      .AllOnlinePlayers.OfType<IServerPlayer>()
      .Where(p =>
        p != byPlayer
        && p.ConnectionState == EnumClientState.Playing
        && p.Entity?.Pos.SquareDistanceTo(x, y, z) <= range * range
      )
      .ToArray();
    if (listeners.Length == 0)
      return;
    _serverChannel.SendPacket(
      new SoundPacket {
        Sound = sound.ToString(),
        X = x,
        Y = y,
        Z = z,
        RandomizePitch = randomizePitch,
        Range = range,
        Volume = volume,
      },
      listeners
    );
  }

  private static void PlayOnClient(
    IWorldAccessor world,
    double x,
    double y,
    double z,
    AssetLocation sound,
    bool randomizePitch,
    float range,
    float volume
  ) {
    float scaled = volume * MachineVolume;
    if (scaled <= 0f)
      return;
#if GAME_GE_1_22
    world.PlaySoundAt(
      new SoundAttributes(sound, randomizePitch)
      {
        Type = EnumSoundType.Sound,
        Range = range,
      },
      x,
      y,
      z,
      0,
      null,
      scaled
    );
#else
    world.PlaySoundAt(sound, x, y, z, null, randomizePitch, range, scaled);
#endif
  }
}
