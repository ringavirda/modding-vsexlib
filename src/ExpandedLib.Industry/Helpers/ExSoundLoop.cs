using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Helpers;

/// <summary>One machine's looping sound, loaded once on the client and released for good by
/// <see cref="Dispose"/>. Inert on the server.</summary>
/// <remarks><see cref="Update"/> loads it the first time the machine runs, starts and stops it with
/// the machine, and re-applies <see cref="Volume"/> times <see cref="ExSounds.MachineVolume"/> and
/// <see cref="Pitch"/> when either changes. A loaded loop also takes a new
/// <see cref="ExSounds.MachineVolume"/> the moment it is set, without an <see cref="Update"/>;
/// <see cref="ExSounds"/> holds it only weakly from load to <see cref="Dispose"/>. A disposed loop
/// never loads again. The owner calls <see cref="Dispose"/> from both <c>OnBlockRemoved</c> and
/// <c>OnBlockUnloaded</c>. Client main thread only, as are the block entity calls that drive
/// it.</remarks>
public sealed class ExSoundLoop : IDisposable {
  private readonly AssetLocation _sound;
  private readonly float _range;
  private ILoadedSound? _loaded;
  private float _appliedVolume;
  private float _appliedPitch;

  /// <param name="sound">The clip, looped gaplessly.</param>
  /// <param name="volume">Volume before <see cref="ExSounds.MachineVolume"/>, 0-1.</param>
  /// <param name="range">Audible distance, blocks.</param>
  /// <param name="pitch">Playback pitch, 1 = as recorded.</param>
  public ExSoundLoop(
    AssetLocation sound,
    float volume = 1f,
    float range = 16f,
    float pitch = 1f
  ) {
    _sound = sound;
    Volume = volume;
    _range = range;
    Pitch = pitch;
  }

  /// <summary>Volume before <see cref="ExSounds.MachineVolume"/>, 0-1; applied on the next
  /// <see cref="Update"/>.</summary>
  public float Volume { get; set; }

  /// <summary>Playback pitch, 1 = as recorded; applied on the next <see cref="Update"/>.</summary>
  public float Pitch { get; set; }

  /// <summary>True once <see cref="Dispose"/> has run.</summary>
  public bool IsDisposed { get; private set; }

  /// <summary>True while the loaded sound is playing.</summary>
  public bool IsPlaying => _loaded?.IsPlaying == true;

  /// <summary>Plays the loop at <paramref name="pos"/> while <paramref name="running"/>, stops it
  /// otherwise. Does nothing on the server or after <see cref="Dispose"/>. The position is fixed
  /// by the first call that loads the sound.</summary>
  public void Update(ICoreAPI? api, BlockPos pos, bool running) {
    if (IsDisposed || api is not ICoreClientAPI)
      return;
    if (!running) {
      if (_loaded is { IsPlaying: true })
        _loaded.Stop();
      return;
    }
    if (_loaded == null) {
      _loaded = ExSounds.CreateLoop(api, pos, _sound, Volume, _range, Pitch);
      _appliedVolume = Volume * ExSounds.MachineVolume;
      _appliedPitch = Pitch;
      if (_loaded == null)
        return;
      ExSounds.Track(this);
    }
    ApplyVolume();
    if (Pitch != _appliedPitch) {
      _loaded.SetPitch(Pitch);
      _appliedPitch = Pitch;
    }
    if (!_loaded.IsPlaying)
      _loaded.Start();
  }

  /// <summary>Stops and releases the sound; later <see cref="Update"/> calls do nothing.</summary>
  public void Dispose() {
    IsDisposed = true;
    if (_loaded == null)
      return;
    ExSounds.Untrack(this);
    _loaded.Stop();
    _loaded.Dispose();
    _loaded = null;
  }

  /// <summary>Sets the loaded sound to <see cref="Volume"/> times
  /// <see cref="ExSounds.MachineVolume"/> when that differs from what it plays at; does nothing
  /// before load or after <see cref="Dispose"/>.</summary>
  internal void ApplyVolume() {
    if (_loaded == null)
      return;
    float volume = Volume * ExSounds.MachineVolume;
    if (volume == _appliedVolume)
      return;
    _loaded.SetVolume(volume);
    _appliedVolume = volume;
  }
}
