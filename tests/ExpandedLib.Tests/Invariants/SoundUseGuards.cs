using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="SoundUse"/> over exlib's own sources and assemblies, and against fixtures
/// that each break one of its rules.</summary>
public class SoundUseGuards {
  #region exlib

  // Fails when a call site in src/ repeats a sound faster than its clip, e.g. BlockEntityPipe's
  // Lava ambience given interval 0.
  [Fact]
  public void Exlibs_repeating_one_shots_never_outpace_their_clips() {
    string[] files = Directory
      .EnumerateFiles(
        Path.Combine(RepoPaths.Root, "src"),
        "*.cs",
        SearchOption.AllDirectories
      )
      .Where(f =>
        !f.Contains("/bin/", StringComparison.Ordinal)
        && !f.Contains("/obj/", StringComparison.Ordinal)
      )
      .ToArray();

    IReadOnlyList<string> offenders = SoundUse.ShortRepeats(files);

    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    Assert.Contains(
      files,
      f => File.ReadAllText(f).Contains("ExSounds.PlayLoop(")
    );
  }

  // Fails when the guard's allowlist loses ExSounds.cs, or an exlib file outside it plays a sound
  // through world.PlaySoundAt.
  [Fact]
  public void Exlibs_sounds_all_go_through_ExSounds() {
    string[] files = Directory
      .EnumerateFiles(
        Path.Combine(RepoPaths.Root, "src"),
        "*.cs",
        SearchOption.AllDirectories
      )
      .Where(f =>
        !f.Contains("/bin/", StringComparison.Ordinal)
        && !f.Contains("/obj/", StringComparison.Ordinal)
      )
      .ToArray();

    IReadOnlyList<string> offenders = SoundUse.DirectSounds(files);

    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    Assert.Contains(
      files,
      f =>
        f.EndsWith("ExSounds.cs", StringComparison.Ordinal)
        && File.ReadAllText(f).Contains(".PlaySoundAt(")
    );
  }

  // Fails when an exlib type holds an ILoadedSound itself, or an ExSoundLoop it leaves loaded on
  // removal or unload.
  [Fact]
  public void Exlibs_sound_holders_release_on_removal_and_unload() {
    var offenders = SoundUse
      .UndisposedLoops(typeof(ExSounds).Assembly)
      .Concat(SoundUse.UndisposedLoops(typeof(ExpandedLibModSystem).Assembly))
      .ToList();

    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  #endregion

  #region Rules

  // Fails when ShortRepeats stops comparing a literal interval with the clip.
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.ShortRepeats))]
  public void A_literal_interval_shorter_than_the_clip_is_named() {
    IReadOnlyList<string> offenders = Scan(
      "ExSounds.PlayThrottled(Api, Pos, ExSounds.Fire, ref _ms, 5000, 0.6f);"
    );

    Assert.Contains(
      "repeats every 5000 ms but lasts 9260 ms",
      Assert.Single(offenders)
    );
  }

  // Fails when ShortRepeats accepts any ClipLengthMs, not only the played sound's.
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.ShortRepeats))]
  public void The_clip_length_of_another_sound_is_named() {
    IReadOnlyList<string> offenders = Scan(
      "ExSounds.PlayLoop(\n  world,\n  pos,\n  ExSounds.Lava,\n  ref ms,\n"
        + "  ExSounds.ClipLengthMs(ExSounds.Creek)\n);"
    );

    Assert.Contains("not of the sound played", Assert.Single(offenders));
  }

  // Fails when ShortRepeats trusts a sound it cannot look up.
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.ShortRepeats))]
  public void A_sound_outside_the_catalogue_is_named() {
    IReadOnlyList<string> offenders = Scan(
      "ExSounds.PlayLoop(world, pos, mySound, ref ms, 20000);"
    );

    Assert.Contains("is not an ExSounds constant", Assert.Single(offenders));
  }

  // Fails when ShortRepeats treats a named constant as long enough.
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.ShortRepeats))]
  public void An_interval_it_cannot_read_is_named() {
    IReadOnlyList<string> offenders = Scan(
      "ExSounds.PlayLoop(world, pos, ExSounds.Latch, ref ms, SomeMs);"
    );

    Assert.Contains("neither a literal", Assert.Single(offenders));
  }

  // Fails when ShortRepeats flags a literal at least the clip length, or the played sound's own
  // ClipLengthMs.
  [Fact]
  public void A_long_enough_literal_or_the_same_clip_length_passes() {
    Assert.Empty(
      Scan(
        "ExSounds.PlayThrottled(Api, Pos, ExSounds.Fire, ref a, 9260, volume: 0.3f);\n"
          + "ExSounds.PlayLoop(w, p, ExSounds.Lava, ref b, ExSounds.ClipLengthMs(ExSounds.Lava));"
      )
    );
  }

  // Fails when UndisposedLoops misses a raw ILoadedSound, OnBlockUnloaded's call into Release, a
  // wrong-field access, or a null-conditional Dispose (ReceiverUse returning its argument).
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.UndisposedLoops))]
  public void Undisposed_and_raw_sound_holders_are_named() {
    List<string> offenders = SoundUse
      .UndisposedLoops(typeof(SoundUseGuards).Assembly)
      .Where(o => o.Contains("SoundFixture", StringComparison.Ordinal))
      .ToList();

    Assert.Equal(5, offenders.Count);
    Assert.Contains(
      offenders,
      o => o.Contains("RawSoundFixture._sound: holds an ILoadedSound")
    );
    Assert.Contains(
      offenders,
      o =>
        o.Contains(
          "UnloadMissingSoundFixture._loop: no OnBlockUnloaded() override"
        )
    );
    Assert.Contains(
      offenders,
      o =>
        o.Contains(
          "UnloadForgetsSoundFixture._loop: OnBlockUnloaded() never disposes it"
        )
    );
    Assert.Contains(
      offenders,
      o =>
        o.Contains(
          "UnloadTouchesOtherSoundFixture._loop: OnBlockUnloaded() never disposes it"
        )
    );
  }

  // Fails when DirectSounds stops matching PlaySoundAt or LoadSound, or reads a commented-out call.
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.DirectSounds))]
  public void A_direct_play_or_load_is_named_and_a_comment_is_not() {
    IReadOnlyList<string> offenders = Scan(
      "Api.World.PlaySoundAt(ExSounds.Fire, Pos.X, Pos.Y, Pos.Z);\n"
        + "  // world.PlaySoundAt(sound, x, y, z);\n"
        + "ILoadedSound s = capi.World.LoadSound(p);",
      SoundUse.DirectSounds
    );

    Assert.Equal(2, offenders.Count);
    Assert.EndsWith(
      ":1: PlaySoundAt called directly; use ExSounds",
      offenders[0]
    );
    Assert.EndsWith(
      ":3: LoadSound called directly; use ExSounds",
      offenders[1]
    );
  }

  // Fails when UndisposedLoops accepts any read of the loop field in place of a Dispose call on it.
  [Fact]
  [PlantedDefect(typeof(SoundUse), nameof(SoundUse.UndisposedLoops))]
  public void A_loop_its_unload_only_stops_is_named() {
    Assert.Contains(
      "ExpandedLib.Tests.UnloadStopsSoundFixture._loop: OnBlockUnloaded() never disposes it",
      SoundUse.UndisposedLoops(typeof(SoundUseGuards).Assembly)
    );
  }

  #endregion

  private static IReadOnlyList<string> Scan(
    string source,
    System.Func<IEnumerable<string>, IReadOnlyList<string>>? guard = null
  ) {
    string file = Path.Combine(
      Path.GetTempPath(),
      $"sounduse-{Guid.NewGuid():N}.cs"
    );
    File.WriteAllText(file, source);
    try {
      return (guard ?? SoundUse.ShortRepeats)([file]);
    } finally {
      File.Delete(file);
    }
  }
}

internal sealed class RawSoundFixture : BlockEntity {
  private ILoadedSound? _sound;

  public override void OnBlockRemoved() => _sound?.Dispose();

  public override void OnBlockUnloaded() => _sound?.Dispose();
}

internal sealed class UnloadMissingSoundFixture : BlockEntity {
  private readonly ExSoundLoop _loop = new(ExSounds.Fire);

  public override void OnBlockRemoved() => _loop.Dispose();
}

internal sealed class UnloadForgetsSoundFixture : BlockEntity {
  private readonly ExSoundLoop _loop = new(ExSounds.Fire);

  public override void OnBlockRemoved() => Release();

  public override void OnBlockUnloaded() => base.OnBlockUnloaded();

  private void Release() => _loop.Dispose();
}

internal sealed class UnloadTouchesOtherSoundFixture : BlockEntity {
  private readonly ExSoundLoop _loop = new(ExSounds.Fire);
  private int _ticks;

  public override void OnBlockRemoved() => _loop.Dispose();

  public override void OnBlockUnloaded() => _ticks = 0;

  internal int Ticks => _ticks;
}

internal sealed class DisposingSoundFixture : BlockEntity {
  private readonly ExSoundLoop _loop = new(ExSounds.Fire);

  public override void OnBlockRemoved() => Release();

  public override void OnBlockUnloaded() => Release();

  private void Release() => _loop.Dispose();
}

internal sealed class UnloadStopsSoundFixture : BlockEntity {
  private readonly ExSoundLoop _loop = new(ExSounds.Fire);

  public override void OnBlockRemoved() => _loop.Dispose();

  public override void OnBlockUnloaded() => _loop.Update(Api, Pos, false);
}

internal sealed class NullableLoopSoundFixture : BlockEntity {
  private ExSoundLoop? _loop = new(ExSounds.Fire);

  public override void OnBlockRemoved() => _loop?.Dispose();

  public override void OnBlockUnloaded() => _loop?.Dispose();

  internal void Drop() => _loop = null;
}
