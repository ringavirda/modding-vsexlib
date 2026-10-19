namespace ExpandedLib.Machines;

/// <summary>Where a <see cref="CraftSequenceRun"/> plays what a held step shows and sounds: the
/// swing on the holder and the sound at each stroke.</summary>
public interface ICraftEffects {
  /// <summary>Starts an animation on the holder at the start of a stroke.</summary>
  /// <param name="animation">The entity animation code.</param>
  void StartAnimation(string animation);

  /// <summary>Stops an animation started by <see cref="StartAnimation"/> when the hold ends.</summary>
  /// <param name="animation">The entity animation code.</param>
  void StopAnimation(string animation);

  /// <summary>Plays a sound at the holder at the strike of a stroke.</summary>
  /// <param name="sound">The sound's asset path, such as <c>sounds/effect/anvilhit</c>.</param>
  void PlaySound(string sound);
}
