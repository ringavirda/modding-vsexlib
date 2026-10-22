using ExpandedLib.Catalogues;
using ExpandedLib.Machines;

namespace ExpandedLib.Blocks;

/// <summary>The swings a player has put into paying one tool stage: the hits counted, which survive
/// the player letting go, and the stroke clock of the hold in progress. Used from one thread.</summary>
internal sealed class ConstructionHold {
  private int _stage = -1;
  private int _step = -1;
  private string? _playing;
  private float _held;
  private float _lastUsed;
  private int _swings;
  private int _strikes;
  private int _hits;
  private int _done;

  /// <summary>Hits counted on the stage, from 0.</summary>
  public int Struck => _hits;

  /// <summary>Whether a hold begun by <see cref="Begin"/> has neither ended nor been released.</summary>
  public bool Active { get; private set; }

  /// <summary>Starts a hold on step <paramref name="step"/> of stage <paramref name="stage"/>; the hits
  /// counted on the same step by earlier holds stay, those of another step or stage drop.</summary>
  /// <param name="stage">The index of the stage being paid.</param>
  /// <param name="step">The index of the stage's tool step being paid, from 0.</param>
  public void Begin(int stage, int step) {
    if (stage != _stage || step != _step)
      _hits = 0;
    _stage = stage;
    _step = step;
    Active = true;
    _held = 0f;
    _lastUsed = 0f;
    _swings = 0;
    _strikes = 0;
    _done = _hits;
  }

  /// <summary>Carries the hold to <paramref name="secondsUsed"/>, starting the animation of each stroke and
  /// playing the sound of each strike reached, and counting a hit at each finished swing.</summary>
  /// <param name="secondsUsed">Seconds since the hold began, as the game passes them.</param>
  /// <param name="step">The stage's step; its <see cref="CraftStep.Hits"/> is above 0.</param>
  /// <param name="tool">The held tool's stroke defaults; null for none.</param>
  /// <param name="effects">Receives the animation and sound; null plays none.</param>
  /// <returns>Whether the stage has all its hits.</returns>
  public bool Advance(
    float secondsUsed,
    CraftStep step,
    CraftToolDefault? tool,
    ICraftEffects? effects
  ) {
    _held += secondsUsed - _lastUsed;
    _lastUsed = secondsUsed;
    float stroke = tool?.Stroke ?? CraftToolDefault.DefaultStroke;
    Stroke(step, tool, stroke, effects);
    while (_hits < step.Hits && _held >= (_hits - _done + 1) * stroke)
      _hits++;
    return _hits >= step.Hits;
  }

  /// <summary>Ends the animation the hold started and drops the swing left unfinished; the hits counted
  /// stay.</summary>
  /// <param name="effects">The effects the hold played on; null for none.</param>
  public void Release(ICraftEffects? effects) {
    if (_playing != null)
      effects?.StopAnimation(_playing);
    _playing = null;
    _held = 0f;
    _swings = 0;
    _strikes = 0;
    _done = _hits;
    Active = false;
  }

  /// <summary>Ends the hold and drops the hits counted.</summary>
  /// <param name="effects">The effects the hold played on; null for none.</param>
  public void Clear(ICraftEffects? effects) {
    _hits = 0;
    Release(effects);
  }

  private void Stroke(
    CraftStep step,
    CraftToolDefault? tool,
    float stroke,
    ICraftEffects? effects
  ) {
    if (effects == null)
      return;
    string? animation = step.Animation ?? tool?.Animation;
    string? sound = step.Sound ?? tool?.Sound;
    float strike = tool?.Strike ?? CraftToolDefault.DefaultStrike;
    int left = step.Hits - _done;

    while (animation != null && _swings < left && _swings * stroke <= _held) {
      effects.StartAnimation(animation);
      _playing = animation;
      _swings++;
    }
    while (
      sound != null && _strikes < left && (_strikes * stroke) + strike <= _held
    ) {
      effects.Strike(sound);
      _strikes++;
    }
  }
}
