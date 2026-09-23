using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>A real <see cref="ILogger"/> that keeps every entry it is handed, formatted once
/// through <see cref="System.String.Format(string, object[])"/>. <see cref="TestWorld"/> wires one
/// instance as both <c>Api.Logger</c> and <c>World.Logger</c>.</summary>
/// <remarks>Every instance registers itself when it is created and stays referenced until the next
/// <see cref="FailOnWarningsAttribute"/> check takes it; in an assembly without that attribute the
/// registration is never read.</remarks>
public sealed class RecordingLogger : LoggerBase {
  private static readonly List<RecordingLogger> Created = [];

  /// <summary>Every entry logged so far, oldest first, format and args already merged.</summary>
  public IReadOnlyList<(EnumLogType Type, string Message)> Entries => _entries;

  private readonly List<(EnumLogType Type, string Message)> _entries = [];

  // Every entry ever logged; Clear leaves it, so a cleared Warning still reaches the check.
  private readonly List<(EnumLogType Type, string Message)> _history = [];

  private readonly List<(EnumLogType Type, string Fragment)> _expected = [];

  /// <summary>A logger with no entries, registered for the next
  /// <see cref="FailOnWarningsAttribute"/> check.</summary>
  public RecordingLogger() {
    lock (Created)
      Created.Add(this);
  }

  /// <summary>The message text of every <see cref="EnumLogType.Error"/> entry, oldest first.</summary>
  public IEnumerable<string> Errors => Select(EnumLogType.Error);

  /// <summary>The message text of every <see cref="EnumLogType.Warning"/> entry, oldest first.</summary>
  public IEnumerable<string> Warnings => Select(EnumLogType.Warning);

  private IEnumerable<string> Select(EnumLogType type) {
    foreach ((EnumLogType Type, string Message) entry in _entries)
      if (entry.Type == type)
        yield return entry.Message;
  }

  /// <summary>Empties <see cref="Entries"/>, for a test that wants a clean slate mid-run. A
  /// <see cref="FailOnWarningsAttribute"/> check still sees the cleared entries.</summary>
  public void Clear() => _entries.Clear();

  /// <summary>Declares that this logger receives at least one entry of <paramref name="type"/>
  /// whose message contains <paramref name="fragment"/> (ordinal) during the current test.</summary>
  /// <remarks>Read only by a <see cref="FailOnWarningsAttribute"/> check: every entry it matches
  /// is expected, however many, and an expectation no entry matched fails the test. May be called
  /// before or after the entry is logged.</remarks>
  /// <param name="type">The entry's exact log type.</param>
  /// <param name="fragment">Text the message must contain; an empty string matches any message of
  /// <paramref name="type"/>.</param>
  /// <exception cref="ArgumentNullException"><paramref name="fragment"/> is null.</exception>
  public void Expect(EnumLogType type, string fragment) {
    ArgumentNullException.ThrowIfNull(fragment);
    _expected.Add((type, fragment));
  }

  /// <summary>Takes every logger created since the previous call and returns one line per
  /// unexpected Warning, Error or Fatal entry and per expectation no entry matched; empty when
  /// clean. The taken loggers are no longer registered.</summary>
  internal static List<string> TakeFaults() {
    RecordingLogger[] loggers;
    lock (Created) {
      loggers = [.. Created];
      Created.Clear();
    }
    var faults = new List<string>();
    foreach (RecordingLogger logger in loggers)
      logger.AddFaults(faults);
    return faults;
  }

  private void AddFaults(List<string> faults) {
    var matched = new bool[_expected.Count];
    foreach ((EnumLogType type, string message) in _history) {
      bool expected = false;
      for (int i = 0; i < _expected.Count; i++)
        if (
          _expected[i].Type == type
          && message.Contains(_expected[i].Fragment, StringComparison.Ordinal)
        ) {
          matched[i] = true;
          expected = true;
        }
      if (
        !expected
        && type is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal
      )
        faults.Add($"unexpected {type}: {message}");
    }
    for (int i = 0; i < _expected.Count; i++)
      if (!matched[i])
        faults.Add(
          $"expected {_expected[i].Type} containing \"{_expected[i].Fragment}\" was never logged"
        );
  }

  /// <summary><see cref="LoggerBase"/>'s one abstract member: every other overload (the
  /// message-only ones, the exception ones) already forwards here.</summary>
  protected override void LogImpl(
    EnumLogType logType,
    string format,
    params object[] args
  ) {
    var entry = (
      logType,
      args is { Length: > 0 } ? string.Format(format, args) : format
    );
    _entries.Add(entry);
    _history.Add(entry);
  }
}
