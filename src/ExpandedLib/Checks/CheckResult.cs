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
  /// <summary>The findings an <see cref="ExlibChecks.Exempt"/> exemption took out of
  /// <see cref="Errors"/>, each followed by its reason; empty when none.</summary>
  public IReadOnlyList<string> Exempted { get; init; } = [];
}
