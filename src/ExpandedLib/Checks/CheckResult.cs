using System.Collections.Generic;

namespace ExpandedLib.Checks;

/// <summary>One check's findings for one domain; empty <paramref name="Errors"/> means the check
/// found nothing wrong.</summary>
/// <param name="Check">The check's name, e.g. <c>"RecipeCodes"</c>.</param>
/// <param name="Domain">The mod domain examined.</param>
/// <param name="Errors">One readable line per violation found.</param>
public sealed record CheckResult(
  string Check,
  string Domain,
  IReadOnlyList<string> Errors
) {
  /// <summary>The findings an <see cref="ExlibChecks.Exempt(string, string, string[], string)"/>
  /// exemption took out of <see cref="Errors"/>, each followed by its reason; empty when
  /// none.</summary>
  public IReadOnlyList<string> Exempted { get; init; } = [];

  /// <summary>What a finding about more than one thing is about, keyed by its line in
  /// <see cref="Errors"/>: the two recipes of a collision, the two codes of a prefix clash, each
  /// written as the line writes it. An exemption takes such a finding only when it names every
  /// subject. Empty when no finding is about more than one thing.</summary>
  public IReadOnlyDictionary<
    string,
    IReadOnlyList<string>
  > Subjects { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
}
