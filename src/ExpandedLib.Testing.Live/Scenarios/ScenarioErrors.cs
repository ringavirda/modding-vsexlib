using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary>The server log entries a case watches (scenarios.md, Errors and exceptions): an
/// error or fatal entry its <c>allowErrors</c> does not match, and the orphaned-block-entity
/// notice, fail the case; warnings are only listed.</summary>
internal sealed class ScenarioErrors(IReadOnlyList<Regex> allowed) {
  private const string Recreated = "orphaned block entit";

  private readonly List<string> _errors = [];
  private readonly List<string> _warnings = [];
  private readonly object _gate = new();
  private int _reported;

  /// <summary>Every entry that fails the case, in order.</summary>
  public IReadOnlyList<string> Errors {
    get {
      lock (_gate)
        return [.. _errors];
    }
  }

  public IReadOnlyList<string> Warnings {
    get {
      lock (_gate)
        return [.. _warnings];
    }
  }

  /// <summary>Takes one log entry from any thread; <paramref name="text"/> is its formatted
  /// message.</summary>
  public void Add(EnumLogType type, string text) {
    if (text.StartsWith(ScenarioOutput.Prefix))
      return;
    bool recreated =
      type == EnumLogType.Notification
      && text.Contains("Recreated")
      && text.Contains(Recreated);
    lock (_gate)
      if (type is EnumLogType.Error or EnumLogType.Fatal || recreated) {
        if (!allowed.Any(r => r.IsMatch(text)))
          _errors.Add(text);
      } else if (type == EnumLogType.Warning)
        _warnings.Add(text);
  }

  /// <summary>The first failing entry not yet taken, or null.</summary>
  public string? TakeNew() {
    lock (_gate)
      return _reported < _errors.Count ? _errors[_reported++] : null;
  }
}
