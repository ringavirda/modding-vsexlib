using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Assembling;

/// <summary>Which of the offered placements the client shows: a click steps to the next, a recompute keeps the shown one while it is still offered. Used from one thread.</summary>
internal sealed class HandAssemblyStepper {
  private string _target = "";
  private IReadOnlyList<string> _offered = [];
  private int _index;

  /// <summary>How many placements are offered.</summary>
  public int Count => _offered.Count;

  /// <summary>The shown placement's position in the list, from 0.</summary>
  public int Index => _index;

  /// <summary>The code of the shown placement; null when none is offered.</summary>
  public string? Shown => _index < _offered.Count ? _offered[_index] : null;

  /// <summary>Takes a recompute of the offered placements.</summary>
  /// <param name="target">Names the cell and face aimed; a different one starts at the first placement.</param>
  /// <param name="offered">The codes of the placements now offered, in order.</param>
  public void Retarget(string target, IReadOnlyList<string> offered) {
    string? shown = target == _target ? Shown : null;
    _target = target;
    _offered = offered;
    int at = shown == null ? -1 : offered.ToList().IndexOf(shown);
    _index = at < 0 ? 0 : at;
  }

  /// <summary>Moves to the next placement, wrapping after the last.</summary>
  /// <returns>The code now shown; null when none is offered.</returns>
  public string? Step() {
    if (_offered.Count == 0)
      return null;
    _index = (_index + 1) % _offered.Count;
    return Shown;
  }
}
