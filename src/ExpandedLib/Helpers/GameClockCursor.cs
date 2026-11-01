using Vintagestory.API.Datastructures;

namespace ExpandedLib.Helpers;

/// <summary>Remembers the last calendar hour a machine saw and reports the game hours elapsed since. Not thread safe; one cursor belongs to one ticking owner.</summary>
public sealed class GameClockCursor {
  /// <summary>The last calendar time handed to <see cref="Advance(double)"/>, in game hours since the world's start; negative when none is held.</summary>
  public double LastHours { get; private set; } = -1d;

  /// <summary>Moves the cursor to <paramref name="nowHours"/> and reports the span it crossed.</summary>
  /// <param name="nowHours">The calendar time in game hours since the world's start; negative or NaN when the calendar is missing.</param>
  /// <returns>Game hours elapsed since the previous call, 0 or more. 0 on the first call, after a call with a missing calendar, and when the clock went back; a clock that went back becomes the new last hour.</returns>
  public double Advance(double nowHours) {
    if (double.IsNaN(nowHours) || nowHours < 0d) {
      LastHours = -1d;
      return 0d;
    }
    double last = LastHours;
    LastHours = nowHours;
    return last >= 0d && nowHours > last ? nowHours - last : 0d;
  }

  /// <summary>Forgets the last hour, so the next <see cref="Advance(double)"/> returns 0.</summary>
  public void Reset() => LastHours = -1d;

  /// <summary>Writes the last hour under <paramref name="key"/>.</summary>
  /// <param name="tree">The attribute tree to write into.</param>
  /// <param name="key">The attribute name.</param>
  public void Save(ITreeAttribute tree, string key) =>
    tree.SetDouble(key, LastHours);

  /// <summary>Reads the last hour from <paramref name="key"/>; a tree without it leaves the cursor empty.</summary>
  /// <param name="tree">The attribute tree to read from.</param>
  /// <param name="key">The attribute name.</param>
  public void Load(ITreeAttribute tree, string key) =>
    LastHours = tree.GetDouble(key, -1d);
}
