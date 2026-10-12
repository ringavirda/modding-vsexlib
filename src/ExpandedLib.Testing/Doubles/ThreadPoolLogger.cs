using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>The logger <see cref="TyronThreadPool.Logger"/> holds while the harness runs the game's
/// loaders. A pool worker that throws is logged through it; unset, the log call throws inside the
/// pool's async void catch and ends the test host. Entries go to standard error and are not kept.</summary>
internal sealed class ThreadPoolLogger : LoggerBase {
  protected override void LogImpl(
    EnumLogType logType,
    string format,
    params object[] args
  ) {
    string message = args is { Length: > 0 }
      ? string.Format(format, args)
      : format;
    Console.Error.WriteLine($"[{logType}] {message}");
  }
}
