using System;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;

namespace ExpandedLib.Assembling;

/// <summary>The hold that fixes or takes off a part: its clock, the tool's strokes while it runs, and what cancels it. Used from one thread.</summary>
internal sealed class HandAssemblyHold {
  /// <summary>Seconds a hold takes when it makes or breaks no joint.</summary>
  public const float NoJointSeconds = 0.5f;

  /// <summary>The sound each strike plays when the tool brings none or the wrench's latch.</summary>
  internal const string StrokeSound = "game:sounds/block/heavymetal-hit";

  private const string LatchSound = "sounds/effect/latch";

  private string _target = "";
  private string _what = "";
  private int _slot;
  private float _seconds;
  private float _elapsed;
  private float _stroke;
  private float _strike;
  private string? _animation;
  private string? _sound;
  private string? _playing;
  private int _swings;
  private int _strikes;

  /// <summary>Whether a hold has begun and neither finished nor been cancelled.</summary>
  public bool Active { get; private set; }

  /// <summary>The fraction of the hold done, 0 to 1; 0 when none is active.</summary>
  public float Progress => Active ? Math.Min(1f, _elapsed / _seconds) : 0f;

  /// <summary>How long a hold takes.</summary>
  /// <param name="joints">Joints made or broken.</param>
  /// <param name="stroke">Seconds of one stroke of the tool; above 0.</param>
  /// <returns>One stroke a joint, <see cref="NoJointSeconds"/> with none.</returns>
  public static float Seconds(int joints, float stroke) =>
    joints > 0 ? joints * stroke : NoJointSeconds;

  /// <summary>Starts a hold.</summary>
  /// <param name="target">Names the cell and face aimed.</param>
  /// <param name="what">Names what the hold does there: the candidate's code, or a take-off.</param>
  /// <param name="slot">The hotbar slot holding the part or tool.</param>
  /// <param name="joints">Joints made or broken.</param>
  /// <param name="tool">The tool's stroke defaults; null for the default stroke with no animation. The strike sound is <see cref="StrokeSound"/> unless the tool brings a sound other than the wrench's latch.</param>
  public void Begin(
    string target,
    string what,
    int slot,
    int joints,
    CraftToolDefault? tool
  ) {
    _target = target;
    _what = what;
    _slot = slot;
    _stroke = tool?.Stroke ?? CraftToolDefault.DefaultStroke;
    _strike = tool?.Strike ?? CraftToolDefault.DefaultStrike;
    _animation = tool?.Animation;
    _sound = tool?.Sound switch {
      null or LatchSound or "game:" + LatchSound => StrokeSound,
      string own => own,
    };
    _seconds = Seconds(joints, _stroke);
    _elapsed = 0f;
    _swings = 0;
    _strikes = 0;
    _playing = null;
    Active = true;
  }

  /// <summary>Carries the hold forward, starting the animation of each stroke and playing the sound of each strike reached.</summary>
  /// <param name="seconds">Seconds since the last call.</param>
  /// <param name="effects">Receives the animation and sound; null plays none.</param>
  /// <returns>True when the hold has run its time; it is then no longer active.</returns>
  public bool Advance(float seconds, ICraftEffects? effects) {
    if (!Active)
      return false;
    _elapsed += seconds;
    while (
      _animation != null
      && _swings * _stroke < _seconds
      && _swings * _stroke <= _elapsed
    ) {
      effects?.StartAnimation(_animation);
      _playing = _animation;
      _swings++;
    }
    while (
      _sound != null
      && _strikes * _stroke < _seconds
      && (_strikes * _stroke) + _strike <= _elapsed
    ) {
      effects?.Strike(_sound);
      _strikes++;
    }
    if (_elapsed < _seconds)
      return false;
    Release(effects);
    return true;
  }

  /// <summary>Cancels the hold and stops the animation it started.</summary>
  /// <param name="effects">The effects the hold played on; null for none.</param>
  public void Release(ICraftEffects? effects) {
    if (_playing != null)
      effects?.StopAnimation(_playing);
    _playing = null;
    Active = false;
  }

  /// <summary>Cancels the hold when the aim, the action or the held slot is no longer the one it began on.</summary>
  /// <param name="target">Names the cell and face aimed now.</param>
  /// <param name="what">Names what the hold would do now.</param>
  /// <param name="slot">The active hotbar slot now.</param>
  /// <param name="effects">The effects the hold played on; null for none.</param>
  /// <returns>Whether the hold is still active.</returns>
  public bool Keep(string target, string what, int slot, ICraftEffects? effects) {
    if (Active && (target != _target || what != _what || slot != _slot))
      Release(effects);
    return Active;
  }
}
