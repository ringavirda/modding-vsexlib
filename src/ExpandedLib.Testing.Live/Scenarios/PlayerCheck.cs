using System;

namespace ExpandedLib.Testing.Live;

/// <summary>One read the scenario player's build makes, tested against what it must hold.</summary>
internal static class PlayerCheck {
  /// <summary>Reads the value with <paramref name="read"/> and tests it with <paramref name="holds"/>.
  /// Returns false with <paramref name="failure"/> as <c>&lt;check&gt;: read &lt;value&gt;</c>, or
  /// <c>&lt;check&gt;: threw &lt;type&gt;: &lt;message&gt;</c>, when it does not hold.</summary>
  public static bool Read<T>(
    string check,
    Func<T> read,
    Func<T, bool> holds,
    out T value,
    out string? failure
  ) {
    failure = null;
    try {
      value = read();
    } catch (Exception e) {
      value = default!;
      failure = $"{check}: threw {e.GetType().Name}: {e.Message}";
      return false;
    }
    if (holds(value))
      return true;
    failure = $"{check}: read {value?.ToString() ?? "null"}";
    return false;
  }
}
