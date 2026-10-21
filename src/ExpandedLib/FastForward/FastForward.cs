using System;
using Vintagestory.API.MathTools;

namespace ExpandedLib.FastForwarding;

/// <summary>The state of the fast-forward frame running on this thread: whether a replay step is running, the calendar time it stands at, and the member whose tick is running. A frame runs synchronously on the server main thread.</summary>
public static class FastForward {
  [ThreadStatic]
  private static bool _replaying;

  [ThreadStatic]
  private static double _cursorHours;

  [ThreadStatic]
  private static FastForwardDriver? _driver;

  [ThreadStatic]
  private static BlockPos? _member;

  /// <summary>Whether the calling thread is inside a replay step.</summary>
  public static bool Replaying => _replaying;

  /// <summary>The calendar time, in game hours since the world's start, the running replay step stands at; 0 outside a replay.</summary>
  public static double CursorHours => _replaying ? _cursorHours : 0.0;

  internal static Frame Replay(double cursorHours) {
    var frame = new Frame(_replaying, _cursorHours, _driver, _member);
    _replaying = true;
    _cursorHours = cursorHours;
    return frame;
  }

  internal static Frame Step(FastForwardDriver driver, BlockPos member) {
    var frame = new Frame(_replaying, _cursorHours, _driver, _member);
    _driver = driver;
    _member = member;
    return frame;
  }

  internal static void RecordLookup(FastForwardDriver driver, BlockPos pos) {
    if (_member != null && ReferenceEquals(_driver, driver))
      driver.Couple(_member, pos);
  }

  internal readonly struct Frame : IDisposable {
    private readonly bool _replaying;
    private readonly double _cursorHours;
    private readonly FastForwardDriver? _driver;
    private readonly BlockPos? _member;

    public Frame(
      bool replaying,
      double cursorHours,
      FastForwardDriver? driver,
      BlockPos? member
    ) {
      _replaying = replaying;
      _cursorHours = cursorHours;
      _driver = driver;
      _member = member;
    }

    public void Dispose() {
      FastForward._replaying = _replaying;
      FastForward._cursorHours = _cursorHours;
      FastForward._driver = _driver;
      FastForward._member = _member;
    }
  }
}
