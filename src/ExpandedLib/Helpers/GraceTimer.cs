using System;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Helpers;

/// <summary>Accumulator for the "hold a condition for N seconds, then fire once" idiom.</summary>
public struct GraceTimer {
  /// <summary>Seconds the condition has held continuously (0 when not counting).</summary>
  public float Elapsed { get; private set; }

  /// <summary>Advances the timer by <paramref name="dt"/> seconds while <paramref name="active"/> is true, returning true once and resetting when <see cref="Elapsed"/> reaches <paramref name="threshold"/>.</summary>
  public bool Update(bool active, float dt, float threshold) {
    if (!active) {
      Elapsed = 0f;
      return false;
    }

    Elapsed += dt;
    if (Elapsed >= threshold) {
      Elapsed = 0f;
      return true;
    }
    return false;
  }

  /// <summary>Clears the accumulator without firing.</summary>
  public void Reset() => Elapsed = 0f;

  /// <summary>Whether the timer is part-way through counting, for HUD warning cues.</summary>
  public readonly bool IsCounting => Elapsed > 0f;

  /// <summary>Seconds left before <paramref name="threshold"/> would fire, for HUD countdowns.</summary>
  public readonly float Remaining(float threshold) =>
    Math.Max(0f, threshold - Elapsed);

  /// <summary>Persists the elapsed time under <paramref name="key"/> so a grace survives reload.</summary>
  public readonly void ToTree(ITreeAttribute tree, string key) =>
    tree.SetFloat(key, Elapsed);

  /// <summary>Restores the elapsed time written by <see cref="ToTree"/>.</summary>
  public void FromTree(ITreeAttribute tree, string key) =>
    Elapsed = tree.GetFloat(key);
}
